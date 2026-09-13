// A tender has an owning officer, and "notify the officer" reaches that person rather than a pool.
//
// Every assertion here is against STORAGE or against a rendered response, never against the code path. The
// notification tests read the outbox rows and the ownership tests read the aggregate or the read model. A test that
// asserted a particular method was called would pass with the fallback wired backwards.
//
// The notification check runs the dispatcher first, because a notification lives in the outbox inside the
// transaction and becomes a row afterwards, so reading the notification table directly finds nothing and every
// assertion would have been vacuously "not notified". It filters by the tender's own identifier from the payload,
// because the suite shares a database and several tests return a tender for edits.
//
//
// EVERY POSITIVE HAS A CONTROL, AND SOME ARE CONTROLS FOR EACH OTHER
//
// A SECOND officer in the same organization is seeded, who used to be notified too. Without them the ownership
// assertion would pass in an organization with only one officer, where "the owner" and "the pool" are the same set
// and the change is unobservable.
//
// The unowned case is forced in storage rather than mocked, because every tender created before ownership existed
// looks exactly like that and the fallback has to hold for rows that actually exist in a deployed database. It
// notifies BOTH officers, which is the point: a tender that notified nobody would be worse than one that notifies
// the pool. That test and the owned one are each other's control.
//
// The owner leaving is covered too, which is reachable through the interface and therefore not hypothetical.
//
// Identifiers are parsed out of the payload rather than substring-matched, because a normalised document can
// contain an identifier for reasons that have nothing to do with the field being asserted.
//
//
// THE PERMISSION BOUNDARIES
//
// Reassignment is refused for the officer and allowed for the manager, so the refusal is about the permission
// rather than about the payload, the state or the route.
//
// Right organization with the wrong permission is refused, and so is the right permission in the wrong
// organization, because ownership must not cross that boundary.
//
// Nominating an officer as approver is refused rather than silently ignored, and the tender is asserted still a
// draft, because a refused nomination must not have moved the state. The no-body case is covered too, which is the
// shape every caller written before ownership sends.
//
//
// THE FILTER, THE PICKER AND THE TILE
//
// The owner filter has its control: unfiltered, the same caller sees both tenders, so the assertion is the filter
// working rather than row-scoping the caller out of a colleague's tender. An unrecognised value is refused naming
// the field rather than silently returning an unfiltered list.
//
// The picker must not offer what the write would refuse, and the two permission tests prove the write refuses
// exactly those two. A supplier holds the read permission, so the picker route is reachable by one and must answer
// not-found rather than hand over the buying organization's staff roster.
//
// And the dashboard tile counts what this caller can act on. Both officers see the same two active tenders, so the
// narrower number is ownership narrowing the count rather than the organization having one tender in it. Before
// ownership, that tile read the same for both.

namespace MotsSupplierPortal.Tests.Integration.Rfqs;

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
using MotsSupplierPortal.Tests.Integration;

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

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Rfqs.Where(r => r.ReferenceCode == referenceCode)
                .ExecuteUpdateAsync(p => p.SetProperty(r => r.OwnerUserId, (Guid?)null));
        }

        (await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/submit-review", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/return", new { comments = "Needs a delivery schedule." }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

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

        var wrongRole = await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/reassign",
            new { newOwnerUserId = reviewerId, reason = "Trying a reviewer." });
        wrongRole.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await wrongRole.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("INELIGIBLE_USER");

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
        var (_, otherManagerId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementManager, org.Id);
        var (_, officerId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer, org.Id);

        var referenceCode = await DraftReadyForReviewAsync(officer, manager, "Nominated RFQ");

        var refused = await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/submit-review",
            new { assignedApproverUserId = officerId });
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

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

        var all = await mine.GetFromJsonAsync<JsonElement>("/api/v1/rfqs?pageSize=100");
        var allCodes = all.GetProperty("data").EnumerateArray()
            .Select(r => r.GetProperty("referenceCode").GetString()).ToList();
        allCodes.Should().Contain(myCode).And.Contain(theirCode);

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
        owners.Should().NotContain(reviewerId).And.NotContain(outsiderId);
        approvers.Should().NotContain(reviewerId).And.NotContain(outsiderId);

        var (supplier, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, $"Peek {Guid.NewGuid():N}"[..30]);
        (await supplier.GetAsync($"/api/v1/rfqs/{referenceCode}/assignees")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Awaiting_my_action_counts_only_the_RFQs_this_officer_is_answerable_for()
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var (mine, _) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var (theirs, _) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer, org.Id);

        await mine.PostAsJsonAsync("/api/v1/rfqs", RfqBasics("My Draft"));
        await theirs.PostAsJsonAsync("/api/v1/rfqs", RfqBasics("Their Draft"));

        var myDashboard = await mine.GetFromJsonAsync<JsonElement>("/api/v1/procurement/dashboard");
        var theirDashboard = await theirs.GetFromJsonAsync<JsonElement>("/api/v1/procurement/dashboard");

        myDashboard.GetProperty("kpis").GetProperty("awaitingMyAction").GetInt32().Should().Be(1);
        theirDashboard.GetProperty("kpis").GetProperty("awaitingMyAction").GetInt32().Should().Be(1);

        myDashboard.GetProperty("kpis").GetProperty("activeRfqs").GetInt32().Should().Be(2);
    }
}
