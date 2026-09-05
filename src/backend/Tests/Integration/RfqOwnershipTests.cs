using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// A-7: an RFQ has an owning officer, and "notify the officer" reaches that person rather than a pool.
///
/// <para>Every assertion here is against STORAGE or against a rendered response, never against the
/// code path: the notification tests read the outbox rows, and the ownership tests read the aggregate
/// or the DTO. A test that asserted "RfqOwnerAsync was called" would pass with the fallback wired
/// backwards.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class RfqOwnershipTests(PostgresApiFixture fixture)
{
    private static object RfqBasics(string titleEn) => new
    {
        titleAr = "طلب اختبار",
        titleEn,
        descriptionAr = (string?)null,
        descriptionEn = (string?)null,
        currencyCode = "SYP",
        publishAt = (DateTimeOffset?)null,
        submissionOpensAt = DateTimeOffset.UtcNow.AddDays(1),
        submissionClosesAt = DateTimeOffset.UtcNow.AddDays(8),
        clarificationDeadlineAt = (DateTimeOffset?)null,
        evaluationTargetDate = (DateTimeOffset?)null,
    };

    private async Task<Guid> CreateActiveTemplateAsync(HttpClient manager)
    {
        var response = await manager.PostAsJsonAsync("/api/v1/evaluation-templates", new { nameAr = "قالب", nameEn = $"Template {Guid.NewGuid():N}" });
        var template = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = template.GetProperty("id").GetGuid();
        await manager.PostAsJsonAsync($"/api/v1/evaluation-templates/{id}/criteria", new
        {
            nameAr = "معيار", nameEn = "Only Criterion", dimension = "Technical", weight = 100, maxScore = 10,
            threshold = (int?)null, scoringType = "Numeric", guidanceAr = (string?)null, guidanceEn = (string?)null,
        });
        await manager.PostAsync($"/api/v1/evaluation-templates/{id}/activate", null);
        return id;
    }

    private async Task<Guid> ActiveSupplierAsync()
    {
        var name = $"Cand {Guid.NewGuid():N}"[..30];
        await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, name);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplier = await db.Suppliers.FirstAsync(s => s.DisplayNameEn == name);
        await db.Suppliers.Where(s => s.Id == supplier.Id).ExecuteUpdateAsync(p => p
            .SetProperty(s => s.OnboardingState, SupplierOnboardingState.Approved)
            .SetProperty(s => s.LifecycleState, SupplierLifecycleState.Active));
        return supplier.Id;
    }

    /// <summary>Create through to Draft-ready-for-review: item, template, one invited supplier.</summary>
    private async Task<string> DraftReadyForReviewAsync(HttpClient officer, HttpClient manager, string titleEn)
    {
        var templateId = await CreateActiveTemplateAsync(manager);

        var created = await officer.PostAsJsonAsync("/api/v1/rfqs", RfqBasics(titleEn));
        created.StatusCode.Should().Be(HttpStatusCode.OK);
        var referenceCode = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;

        await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/items", new
        {
            titleAr = "بند", titleEn = "Item", specificationAr = (string?)null, specificationEn = (string?)null,
            categoryCode = "catering", quantity = 5, unitOfMeasureCode = "unit", isUnitPrice = true, isOptional = false,
        });
        await officer.PutAsJsonAsync($"/api/v1/rfqs/{referenceCode}/evaluation-template", new { evaluationTemplateId = templateId });
        (await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/invitations", new { supplierId = await ActiveSupplierAsync() }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        return referenceCode;
    }

    /// <summary>
    /// Did this user get told about THIS RFQ?
    ///
    /// <para>Through <see cref="NotificationTestHelper"/>, which runs the dispatcher first - a
    /// notification lives in the Outbox inside the transaction (D-5) and becomes a row afterwards, so
    /// reading <c>db.Notifications</c> directly finds nothing and every assertion here would have been
    /// vacuously "not notified".</para>
    ///
    /// <para>Filtered by the RFQ's own id from the payload, because the suite shares a database and
    /// several tests in it return an RFQ for edits.</para>
    /// </summary>
    private async Task<bool> WasNotifiedAsync(Guid userId, string type, string referenceCode)
    {
        Guid rfqId;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            rfqId = await db.Rfqs.Where(r => r.ReferenceCode == referenceCode).Select(r => r.Id).SingleAsync();
        }

        var rows = await NotificationTestHelper.ForRecipientAsync(fixture, userId, type);
        return rows.Any(n => n.DataJson.Contains(rfqId.ToString()));
    }

    [Fact]
    public async Task An_RFQ_is_owned_by_whoever_created_it()
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var (officer, officerId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer, org.Id);

        var created = await officer.PostAsJsonAsync("/api/v1/rfqs", RfqBasics("Owned At Birth"));
        var dto = await created.Content.ReadFromJsonAsync<JsonElement>();

        dto.GetProperty("ownerUserId").GetGuid().Should().Be(officerId);
        dto.GetProperty("ownerName").GetString().Should().Be("Integration Staff");

        // Against storage, not only the response: the DTO could report an owner the row does not carry.
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var code = dto.GetProperty("referenceCode").GetString();
        (await db.Rfqs.SingleAsync(r => r.ReferenceCode == code)).OwnerUserId.Should().Be(officerId);
    }

    [Fact]
    public async Task Returning_for_edits_notifies_the_owner_and_not_the_whole_officer_pool()
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var (officer, ownerId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer, org.Id);
        // The CONTROL: a second officer in the same organization, who used to be notified too. Without
        // them the assertion below would pass on an organization with only one officer, where "the
        // owner" and "the pool" are the same set and the change is unobservable.
        var (_, otherOfficerId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var manager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, org.Id);

        var referenceCode = await DraftReadyForReviewAsync(officer, manager, "Returned RFQ");
        (await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/submit-review", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/return", new { comments = "Add the delivery schedule." }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await WasNotifiedAsync(ownerId, NotificationTypes.RfqReturnedForEdits, referenceCode)).Should().BeTrue();
        (await WasNotifiedAsync(otherOfficerId, NotificationTypes.RfqReturnedForEdits, referenceCode)).Should().BeFalse();
    }

    [Fact]
    public async Task An_unowned_RFQ_still_notifies_the_officer_pool()
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var (officer, ownerId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var (_, otherOfficerId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var manager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, org.Id);

        var referenceCode = await DraftReadyForReviewAsync(officer, manager, "Legacy RFQ");

        // Every RFQ created before A-7 looks exactly like this. Forced in storage rather than mocked,
        // because the fallback has to hold for the rows that actually exist in a deployed database.
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Rfqs.Where(r => r.ReferenceCode == referenceCode)
                .ExecuteUpdateAsync(p => p.SetProperty(r => r.OwnerUserId, (Guid?)null));
        }

        (await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/submit-review", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/return", new { comments = "Needs a delivery schedule." }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // BOTH officers, which is the point: an unowned RFQ that notified nobody would be worse than
        // one that notifies the pool. This is the control for the test above, and vice versa.
        (await WasNotifiedAsync(ownerId, NotificationTypes.RfqReturnedForEdits, referenceCode)).Should().BeTrue();
        (await WasNotifiedAsync(otherOfficerId, NotificationTypes.RfqReturnedForEdits, referenceCode)).Should().BeTrue();
    }

    [Fact]
    public async Task A_deactivated_owner_falls_back_to_the_pool_rather_than_notifying_nobody()
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var (officer, ownerId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var (_, otherOfficerId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var manager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, org.Id);

        var referenceCode = await DraftReadyForReviewAsync(officer, manager, "Orphaned RFQ");
        (await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/submit-review", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        // The owner leaves. T-077 made this reachable through the UI, so it is not hypothetical.
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Users.Where(u => u.Id == ownerId).ExecuteUpdateAsync(p => p.SetProperty(u => u.IsActive, false));
        }

        (await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/return", new { comments = "Still needs the schedule." }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await WasNotifiedAsync(otherOfficerId, NotificationTypes.RfqReturnedForEdits, referenceCode)).Should().BeTrue();
    }

    [Fact]
    public async Task Reassignment_moves_the_owner_writes_an_audit_row_and_tells_the_new_owner()
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var (officer, firstOwnerId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var (_, secondOwnerId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var manager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, org.Id);

        var created = await officer.PostAsJsonAsync("/api/v1/rfqs", RfqBasics("Handover RFQ"));
        var referenceCode = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;

        var reassign = await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/reassign",
            new { newOwnerUserId = secondOwnerId, reason = "The first officer is on extended leave." });
        reassign.StatusCode.Should().Be(HttpStatusCode.OK);
        (await reassign.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ownerUserId").GetGuid().Should().Be(secondOwnerId);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rfq = await db.Rfqs.SingleAsync(r => r.ReferenceCode == referenceCode);
        rfq.OwnerUserId.Should().Be(secondOwnerId);

        var audit = await db.AuditLogs.Where(a => a.AggregateId == rfq.Id && a.Action == "rfq_reassigned").SingleAsync();
        audit.Reason.Should().Be("The first officer is on extended leave.");
        // Parsed, not substring-matched: a normalised JSON string can contain a guid for reasons that
        // have nothing to do with the field being asserted.
        using var changes = JsonDocument.Parse(audit.Changes!);
        changes.RootElement.GetProperty("fromOwnerUserId").GetGuid().Should().Be(firstOwnerId);
        changes.RootElement.GetProperty("toOwnerUserId").GetGuid().Should().Be(secondOwnerId);

        (await WasNotifiedAsync(secondOwnerId, NotificationTypes.RfqReassigned, referenceCode)).Should().BeTrue();
    }

    [Fact]
    public async Task An_officer_cannot_reassign_their_own_RFQ_away()
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var (officer, _) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var (_, otherOfficerId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var manager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, org.Id);

        var created = await officer.PostAsJsonAsync("/api/v1/rfqs", RfqBasics("Not Yours To Give"));
        var referenceCode = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;

        var refused = await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/reassign",
            new { newOwnerUserId = otherOfficerId, reason = "I would rather not." });
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // The control: the same request from the manager is allowed, so the refusal above is about the
        // permission and not about the payload, the state or the route.
        (await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/reassign",
            new { newOwnerUserId = otherOfficerId, reason = "Rebalancing the workload." }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reassigning_to_someone_who_cannot_work_on_RFQs_is_refused_with_the_reason()
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var (officer, _) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var (_, reviewerId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.OnboardingReviewer, org.Id);
        var (_, outsiderId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer,
            (await OrganizationTestHelper.CreateOrganizationAsync(fixture)).Id);
        var manager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, org.Id);

        var created = await officer.PostAsJsonAsync("/api/v1/rfqs", RfqBasics("Eligibility RFQ"));
        var referenceCode = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;

        // Right organization, wrong permission.
        var wrongRole = await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/reassign",
            new { newOwnerUserId = reviewerId, reason = "Trying a reviewer." });
        wrongRole.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await wrongRole.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("INELIGIBLE_USER");

        // Right permission, wrong organization - BRULE-029: ownership must not cross the boundary.
        var wrongOrg = await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/reassign",
            new { newOwnerUserId = outsiderId, reason = "Trying another org's officer." });
        wrongOrg.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_nominated_approver_is_the_only_manager_notified_and_an_ineligible_one_is_refused()
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var (manager, namedManagerId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementManager, org.Id);
        // The control: a second manager who WOULD have been notified before A-7.
        var (_, otherManagerId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementManager, org.Id);
        var (_, officerId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer, org.Id);

        var referenceCode = await DraftReadyForReviewAsync(officer, manager, "Nominated RFQ");

        // An officer cannot approve, so naming one is refused rather than silently ignored.
        var refused = await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/submit-review",
            new { assignedApproverUserId = officerId });
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // The RFQ is still a Draft: a refused nomination must not have moved the state.
        (await officer.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}"))
            .GetProperty("state").GetString().Should().Be(nameof(RfqState.Draft));

        var submitted = await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/submit-review",
            new { assignedApproverUserId = namedManagerId });
        submitted.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await submitted.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("assignedApproverUserId").GetGuid().Should().Be(namedManagerId);

        (await WasNotifiedAsync(namedManagerId, NotificationTypes.RfqSubmittedForReview, referenceCode)).Should().BeTrue();
        (await WasNotifiedAsync(otherManagerId, NotificationTypes.RfqSubmittedForReview, referenceCode)).Should().BeFalse();
    }

    [Fact]
    public async Task An_un_nominated_review_pass_still_notifies_every_manager()
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var (manager, firstManagerId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementManager, org.Id);
        var (_, otherManagerId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementManager, org.Id);

        var referenceCode = await DraftReadyForReviewAsync(officer, manager, "Unnominated RFQ");

        // No body at all - the shape every caller written before A-7 sends.
        (await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/submit-review", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await WasNotifiedAsync(firstManagerId, NotificationTypes.RfqSubmittedForReview, referenceCode)).Should().BeTrue();
        (await WasNotifiedAsync(otherManagerId, NotificationTypes.RfqSubmittedForReview, referenceCode)).Should().BeTrue();
    }

    [Fact]
    public async Task The_owner_filter_narrows_the_buyer_list_and_refuses_a_value_it_does_not_recognise()
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var (mine, _) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var (theirs, _) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer, org.Id);

        var myCode = (await (await mine.PostAsJsonAsync("/api/v1/rfqs", RfqBasics("Mine"))).Content
            .ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;
        var theirCode = (await (await theirs.PostAsJsonAsync("/api/v1/rfqs", RfqBasics("Theirs"))).Content
            .ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;

        var mineOnly = await mine.GetFromJsonAsync<JsonElement>("/api/v1/rfqs?owner=me&pageSize=100");
        var codes = mineOnly.GetProperty("data").EnumerateArray()
            .Select(r => r.GetProperty("referenceCode").GetString()).ToList();
        codes.Should().Contain(myCode);
        codes.Should().NotContain(theirCode);

        // The control: unfiltered, the same caller sees both - so the assertion above is the filter
        // working and not row-scoping the caller out of their colleague's RFQ.
        var all = await mine.GetFromJsonAsync<JsonElement>("/api/v1/rfqs?pageSize=100");
        var allCodes = all.GetProperty("data").EnumerateArray()
            .Select(r => r.GetProperty("referenceCode").GetString()).ToList();
        allCodes.Should().Contain(myCode).And.Contain(theirCode);

        // An unrecognised value is a 422 naming the field, never a silently unfiltered list.
        var refused = await mine.GetAsync("/api/v1/rfqs?owner=grbage");
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .Should().Be("INVALID_FILTER_VALUE");
    }

    [Fact]
    public async Task The_assignee_lists_offer_exactly_who_the_write_would_accept_and_no_supplier_can_read_them()
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var (officer, officerId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var (_, managerId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementManager, org.Id);
        var (_, reviewerId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.OnboardingReviewer, org.Id);
        var (_, outsiderId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer,
            (await OrganizationTestHelper.CreateOrganizationAsync(fixture)).Id);

        var referenceCode = (await (await officer.PostAsJsonAsync("/api/v1/rfqs", RfqBasics("Pickers"))).Content
            .ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;

        var assignees = await officer.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}/assignees");
        var owners = assignees.GetProperty("owners").EnumerateArray().Select(o => o.GetProperty("userId").GetGuid()).ToList();
        var approvers = assignees.GetProperty("approvers").EnumerateArray().Select(a => a.GetProperty("userId").GetGuid()).ToList();

        owners.Should().Contain(officerId);
        approvers.Should().Contain(managerId);
        // The picker must not offer what the write would refuse - the two tests above prove the write
        // refuses exactly these two.
        owners.Should().NotContain(reviewerId).And.NotContain(outsiderId);
        approvers.Should().NotContain(reviewerId).And.NotContain(outsiderId);

        // A supplier holds rfq.read, so this route is reachable by one - and must answer 404 (§9.2)
        // rather than hand over the buying organization's staff roster.
        var (supplier, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, $"Peek {Guid.NewGuid():N}"[..30]);
        (await supplier.GetAsync($"/api/v1/rfqs/{referenceCode}/assignees")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Awaiting_my_action_counts_only_the_RFQs_this_officer_is_answerable_for()
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var (mine, _) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var (theirs, _) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer, org.Id);

        // Both Drafts, whose next action needs rfq.submit_review - which both officers hold. Before
        // A-7 this tile read 2 for each of them.
        await mine.PostAsJsonAsync("/api/v1/rfqs", RfqBasics("My Draft"));
        await theirs.PostAsJsonAsync("/api/v1/rfqs", RfqBasics("Their Draft"));

        var myDashboard = await mine.GetFromJsonAsync<JsonElement>("/api/v1/procurement/dashboard");
        var theirDashboard = await theirs.GetFromJsonAsync<JsonElement>("/api/v1/procurement/dashboard");

        myDashboard.GetProperty("kpis").GetProperty("awaitingMyAction").GetInt32().Should().Be(1);
        theirDashboard.GetProperty("kpis").GetProperty("awaitingMyAction").GetInt32().Should().Be(1);

        // The control, and the reason this tile is not simply "RFQs I own": both see the same TWO
        // active RFQs, so the number above is ownership narrowing the count and not the organization
        // having one RFQ in it.
        myDashboard.GetProperty("kpis").GetProperty("activeRfqs").GetInt32().Should().Be(2);
    }
}
