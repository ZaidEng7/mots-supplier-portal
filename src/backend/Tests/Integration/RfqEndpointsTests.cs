using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Rfqs;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>FEAT-07.1..07.10/FR-RFQ-001..013. Real end-to-end proof of the state machine verified
/// directly against docs/product/BUSINESS-PROCESSES.md §3.1 (RfqTests.cs already proves the
/// aggregate in isolation; this proves the same transitions through the real HTTP surface,
/// permission-guarded, org-scoped, audited).</summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class RfqEndpointsTests(PostgresApiFixture fixture)
{
    private async Task<(HttpClient Officer, HttpClient Manager, Guid OrgId)> ScopedClientsAsync()
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var manager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, org.Id);
        return (officer, manager, org.Id);
    }

    private static object RfqBasics(string titleEn = "Test RFQ", DateTimeOffset? opensAt = null, DateTimeOffset? closesAt = null) => new
    {
        titleAr = "طلب اختبار",
        titleEn,
        descriptionAr = (string?)null,
        descriptionEn = (string?)null,
        currencyCode = "SYP",
        publishAt = (DateTimeOffset?)null,
        submissionOpensAt = opensAt ?? DateTimeOffset.UtcNow.AddDays(1),
        submissionClosesAt = closesAt ?? DateTimeOffset.UtcNow.AddDays(8),
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

    /// <summary>Registers, verifies, and forces a supplier straight to Active - same
    /// forced-transition pattern as OfferingBuyerSearchTests.ActiveSupplierAsync, for the same
    /// reason: these tests are about RFQ invitations, not the onboarding journey.</summary>
    private async Task<(HttpClient Client, Guid SupplierId)> ActiveSupplierAsync(string name)
    {
        var (client, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, name);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplier = await db.Suppliers.FirstAsync(s => s.DisplayNameEn == name);
        await db.Suppliers.Where(s => s.Id == supplier.Id).ExecuteUpdateAsync(p => p
            .SetProperty(s => s.OnboardingState, SupplierOnboardingState.Approved)
            .SetProperty(s => s.LifecycleState, SupplierLifecycleState.Active));

        return (client, supplier.Id);
    }

    /// <summary>Drives an RFQ through create -> item -> template -> invite -> submit -> approve
    /// using the real HTTP endpoints, returning its reference code. The shared setup for every
    /// test that needs an Approved (or later) RFQ. Invites a fresh Active supplier per call -
    /// SubmitForReview now requires >=1 candidate (EPIC-08 gap closed).</summary>
    private async Task<string> CreateApprovedRfqAsync(HttpClient officer, HttpClient manager, string titleEn = "Approved RFQ",
        DateTimeOffset? opensAt = null, DateTimeOffset? closesAt = null)
    {
        var templateId = await CreateActiveTemplateAsync(manager);

        var createResponse = await officer.PostAsJsonAsync("/api/v1/rfqs", RfqBasics(titleEn, opensAt, closesAt));
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var rfq = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var referenceCode = rfq.GetProperty("referenceCode").GetString()!;

        await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/items", new
        {
            titleAr = "بند", titleEn = "Item", specificationAr = (string?)null, specificationEn = (string?)null,
            categoryCode = "catering", quantity = 5, unitOfMeasureCode = "unit", isUnitPrice = true, isOptional = false,
        });
        await officer.PutAsJsonAsync($"/api/v1/rfqs/{referenceCode}/evaluation-template", new { evaluationTemplateId = templateId });

        var (_, supplierId) = await ActiveSupplierAsync($"Candidate {Guid.NewGuid():N}"[..30]);
        var invite = await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/invitations", new { supplierId });
        invite.StatusCode.Should().Be(HttpStatusCode.OK);

        var submit = await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/submit-review", null);
        submit.StatusCode.Should().Be(HttpStatusCode.OK);

        var approve = await manager.PostAsync($"/api/v1/rfqs/{referenceCode}/approve", null);
        approve.StatusCode.Should().Be(HttpStatusCode.OK);

        return referenceCode;
    }

    [Fact]
    public async Task Full_authoring_to_publish_journey_works_end_to_end()
    {
        var (officer, manager, _) = await ScopedClientsAsync();
        var referenceCode = await CreateApprovedRfqAsync(officer, manager, "Full Journey RFQ");

        var afterApprove = await officer.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}");
        afterApprove.GetProperty("state").GetString().Should().Be(nameof(RfqState.Approved));

        var publish = await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/publish", null);
        publish.StatusCode.Should().Be(HttpStatusCode.OK);
        var published = await publish.Content.ReadFromJsonAsync<JsonElement>();
        published.GetProperty("state").GetString().Should().Be(nameof(RfqState.Published));
    }

    [Fact]
    public async Task Create_without_rfq_create_permission_is_forbidden()
    {
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);

        var response = await reviewer.PostAsJsonAsync("/api/v1/rfqs", RfqBasics());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_officer_from_a_different_organization_cannot_see_or_edit_this_rfq()
    {
        var (officer, manager, _) = await ScopedClientsAsync();
        var referenceCode = await CreateApprovedRfqAsync(officer, manager, "Org Scoped RFQ");

        var otherOrg = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var otherOfficer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, otherOrg.Id);

        var getAttempt = await otherOfficer.GetAsync($"/api/v1/rfqs/{referenceCode}");
        getAttempt.StatusCode.Should().Be(HttpStatusCode.NotFound, "cross-org access must read as not-found, not forbidden, so existence is not leaked");

        var editAttempt = await otherOfficer.PostAsync($"/api/v1/rfqs/{referenceCode}/publish", null);
        editAttempt.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SubmitForReview_is_rejected_without_at_least_one_item()
    {
        var (officer, manager, _) = await ScopedClientsAsync();
        var templateId = await CreateActiveTemplateAsync(manager);
        var createResponse = await officer.PostAsJsonAsync("/api/v1/rfqs", RfqBasics("No Items RFQ"));
        var rfq = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var referenceCode = rfq.GetProperty("referenceCode").GetString();
        await officer.PutAsJsonAsync($"/api/v1/rfqs/{referenceCode}/evaluation-template", new { evaluationTemplateId = templateId });

        var submit = await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/submit-review", null);

        submit.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await submit.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("detail").GetString().Should().Contain("at least one RFQ item");
    }

    [Fact]
    public async Task Publish_is_rejected_before_approval()
    {
        var (officer, manager, _) = await ScopedClientsAsync();
        var templateId = await CreateActiveTemplateAsync(manager);
        var createResponse = await officer.PostAsJsonAsync("/api/v1/rfqs", RfqBasics("Unapproved RFQ"));
        var rfq = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var referenceCode = rfq.GetProperty("referenceCode").GetString();

        var publishAttempt = await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/publish", null);

        // §3: "Illegal transitions return 409 Conflict … listing the current state and the allowed
        // next states." This answered 400 until T3-36 built that response.
        publishAttempt.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Return_for_edits_sends_the_rfq_back_to_draft_with_comments_and_a_second_submit_creates_a_new_approval_step()
    {
        var (officer, manager, _) = await ScopedClientsAsync();
        var templateId = await CreateActiveTemplateAsync(manager);
        var createResponse = await officer.PostAsJsonAsync("/api/v1/rfqs", RfqBasics("Return Loop RFQ"));
        var rfq = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var referenceCode = rfq.GetProperty("referenceCode").GetString();
        await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/items", new
        {
            titleAr = "بند", titleEn = "Item", specificationAr = (string?)null, specificationEn = (string?)null,
            categoryCode = "catering", quantity = 1, unitOfMeasureCode = "unit", isUnitPrice = true, isOptional = false,
        });
        await officer.PutAsJsonAsync($"/api/v1/rfqs/{referenceCode}/evaluation-template", new { evaluationTemplateId = templateId });
        var (_, supplierId) = await ActiveSupplierAsync($"Candidate {Guid.NewGuid():N}"[..30]);
        await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/invitations", new { supplierId });
        await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/submit-review", null);

        var returnResponse = await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/return", new { comments = "Please add pricing detail" });
        returnResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var returned = await returnResponse.Content.ReadFromJsonAsync<JsonElement>();
        returned.GetProperty("state").GetString().Should().Be(nameof(RfqState.Draft));

        var resubmit = await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/submit-review", null);
        resubmit.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterResubmit = await resubmit.Content.ReadFromJsonAsync<JsonElement>();
        var approvals = afterResubmit.GetProperty("approvals").EnumerateArray().ToList();
        approvals.Should().HaveCount(2, "OQ-004 interim: the approval chain is an array that grows per review pass");
        approvals.Should().Contain(a => a.GetProperty("stepNo").GetInt32() == 1 && a.GetProperty("decision").GetString() == "Rejected");
    }

    [Fact]
    public async Task Cancel_with_reason_works_from_a_pre_awarded_state_and_requires_manager_permission()
    {
        var (officer, manager, _) = await ScopedClientsAsync();
        var referenceCode = await CreateApprovedRfqAsync(officer, manager, "Cancellable RFQ");

        var officerAttempt = await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/cancel", new { reason = "no longer needed" });
        officerAttempt.StatusCode.Should().Be(HttpStatusCode.Forbidden, "rfq.cancel is a procurement_manager permission, not procurement_officer");

        var cancel = await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/cancel", new { reason = "Budget withdrawn" });
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await cancel.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("state").GetString().Should().Be(nameof(RfqState.Cancelled));
        body.GetProperty("cancelReason").GetString().Should().Be("Budget withdrawn");
    }

    [Fact]
    public async Task Removing_an_item_and_re_adding_produces_a_dense_line_number_sequence()
    {
        var (officer, _, _) = await ScopedClientsAsync();
        var createResponse = await officer.PostAsJsonAsync("/api/v1/rfqs", RfqBasics("Line Numbering RFQ"));
        var rfq = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var referenceCode = rfq.GetProperty("referenceCode").GetString();

        var firstItem = await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/items", new
        {
            titleAr = "1", titleEn = "1", specificationAr = (string?)null, specificationEn = (string?)null,
            categoryCode = "catering", quantity = 1, unitOfMeasureCode = "unit", isUnitPrice = false, isOptional = false,
        });
        var firstBody = await firstItem.Content.ReadFromJsonAsync<JsonElement>();
        var firstItemId = firstBody.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();

        await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/items", new
        {
            titleAr = "2", titleEn = "2", specificationAr = (string?)null, specificationEn = (string?)null,
            categoryCode = "catering", quantity = 1, unitOfMeasureCode = "unit", isUnitPrice = false, isOptional = false,
        });

        var afterRemove = await officer.DeleteAsync($"/api/v1/rfqs/{referenceCode}/items/{firstItemId}");
        afterRemove.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await afterRemove.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();
        items.Should().ContainSingle();
        items[0].GetProperty("lineNo").GetInt32().Should().Be(1);
        items[0].GetProperty("titleEn").GetString().Should().Be("2");
    }

    [Fact]
    public async Task Unknown_category_or_unit_of_measure_is_rejected_with_a_localized_error_code()
    {
        var (officer, _, _) = await ScopedClientsAsync();
        var createResponse = await officer.PostAsJsonAsync("/api/v1/rfqs", RfqBasics("Bad Reference Data RFQ"));
        var rfq = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var referenceCode = rfq.GetProperty("referenceCode").GetString();

        var badCategory = await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/items", new
        {
            titleAr = "بند", titleEn = "Item", specificationAr = (string?)null, specificationEn = (string?)null,
            categoryCode = "not_a_real_category", quantity = 1, unitOfMeasureCode = "unit", isUnitPrice = false, isOptional = false,
        });
        badCategory.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var badCategoryBody = await badCategory.Content.ReadFromJsonAsync<JsonElement>();
        badCategoryBody.GetProperty("code").GetString().Should().Be("INVALID_CATEGORY");

        var badUom = await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/items", new
        {
            titleAr = "بند", titleEn = "Item", specificationAr = (string?)null, specificationEn = (string?)null,
            categoryCode = "catering", quantity = 1, unitOfMeasureCode = "not_a_real_unit", isUnitPrice = false, isOptional = false,
        });
        badUom.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var badUomBody = await badUom.Content.ReadFromJsonAsync<JsonElement>();
        badUomBody.GetProperty("code").GetString().Should().Be("INVALID_UNIT_OF_MEASURE");
    }

    /// <summary>FEAT-07.6/FR-PWF-004: the real proof the scheduled job (not a user action) opens
    /// and closes the submission window on time. Runs RfqTimelineJob directly (same pattern the
    /// codebase already uses for DocumentExpiryJob's own integration tests) rather than waiting on
    /// the real 5-minute Hangfire cadence.</summary>
    [Fact]
    public async Task Scheduled_timeline_job_opens_and_then_closes_the_submission_window_on_time()
    {
        var (officer, manager, _) = await ScopedClientsAsync();
        var referenceCode = await CreateApprovedRfqAsync(officer, manager, "Timeline RFQ",
            opensAt: DateTimeOffset.UtcNow.AddSeconds(1), closesAt: DateTimeOffset.UtcNow.AddSeconds(2));
        await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/publish", null);

        await Task.Delay(TimeSpan.FromSeconds(1.2));
        await RunTimelineJobAsync();

        var afterOpen = await officer.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}");
        afterOpen.GetProperty("state").GetString().Should().Be(nameof(RfqState.SubmissionOpen));

        await Task.Delay(TimeSpan.FromSeconds(1.2));
        await RunTimelineJobAsync();

        var afterClose = await officer.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}");
        afterClose.GetProperty("state").GetString().Should().Be(nameof(RfqState.SubmissionClosed));
    }

    private async Task RunTimelineJobAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var job = scope.ServiceProvider.GetRequiredService<RfqTimelineJob>();
        await job.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Every_transition_writes_an_audit_row()
    {
        var (officer, manager, _) = await ScopedClientsAsync();
        var referenceCode = await CreateApprovedRfqAsync(officer, manager, "Audited RFQ");
        await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/publish", null);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actions = await db.AuditLogs
            .Where(a => a.ReferenceCode == referenceCode)
            .OrderBy(a => a.OccurredAt)
            .Select(a => a.Action)
            .ToListAsync();

        actions.Should().ContainInOrder("rfq_created", "rfq_item_added", "rfq_evaluation_template_bound",
            "rfq_submitted_for_review", "rfq_approved", "rfq_published");
    }

    [Fact]
    public async Task Inviting_a_suspended_supplier_is_refused_by_the_domain()
    {
        var (officer, _, _) = await ScopedClientsAsync();
        var (_, supplierId) = await ActiveSupplierAsync($"Suspended {Guid.NewGuid():N}"[..30]);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Suppliers.Where(s => s.Id == supplierId)
                .ExecuteUpdateAsync(p => p.SetProperty(s => s.LifecycleState, SupplierLifecycleState.Suspended));
        }
        var createResponse = await officer.PostAsJsonAsync("/api/v1/rfqs", RfqBasics("Suspended Invite RFQ"));
        var rfq = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var referenceCode = rfq.GetProperty("referenceCode").GetString();

        var invite = await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/invitations", new { supplierId });

        invite.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await invite.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("SUPPLIER_NOT_ACTIVE");
    }

    [Fact]
    public async Task Inviting_the_same_supplier_twice_is_rejected()
    {
        var (officer, _, _) = await ScopedClientsAsync();
        var (_, supplierId) = await ActiveSupplierAsync($"Twice {Guid.NewGuid():N}"[..30]);
        var createResponse = await officer.PostAsJsonAsync("/api/v1/rfqs", RfqBasics("Duplicate Invite RFQ"));
        var rfq = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var referenceCode = rfq.GetProperty("referenceCode").GetString();
        await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/invitations", new { supplierId });

        var secondInvite = await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/invitations", new { supplierId });

        secondInvite.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await secondInvite.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("detail").GetString().Should().Contain("already been invited");
    }

    [Fact]
    public async Task Publish_is_refused_when_an_invited_supplier_is_no_longer_active()
    {
        var (officer, manager, _) = await ScopedClientsAsync();
        var referenceCode = await CreateApprovedRfqAsync(officer, manager, "Suspended Before Publish RFQ");
        var rfqBeforePublish = await officer.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}");
        var invitedSupplierId = rfqBeforePublish.GetProperty("invitations").EnumerateArray().Single().GetProperty("supplierId").GetGuid();
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Suppliers.Where(s => s.Id == invitedSupplierId)
                .ExecuteUpdateAsync(p => p.SetProperty(s => s.LifecycleState, SupplierLifecycleState.Suspended));
        }

        var publish = await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/publish", null);

        publish.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await publish.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("SUPPLIER_NOT_ACTIVE");
        var stillApproved = await officer.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}");
        stillApproved.GetProperty("state").GetString().Should().Be(nameof(RfqState.Approved));
    }

    [Fact]
    public async Task Candidate_suggestions_rank_by_category_match_and_exclude_already_invited_suppliers()
    {
        var (officer, _, _) = await ScopedClientsAsync();
        var createResponse = await officer.PostAsJsonAsync("/api/v1/rfqs", RfqBasics("Candidates RFQ"));
        var rfq = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var referenceCode = rfq.GetProperty("referenceCode").GetString();
        await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/items", new
        {
            titleAr = "بند", titleEn = "Item", specificationAr = (string?)null, specificationEn = (string?)null,
            categoryCode = "catering", quantity = 1, unitOfMeasureCode = "unit", isUnitPrice = false, isOptional = false,
        });

        var (matchClient, matchSupplierId) = await ActiveSupplierAsync($"Match {Guid.NewGuid():N}"[..30]);
        await matchClient.PostAsJsonAsync("/api/v1/suppliers/me/offerings", new
        {
            nameAr = "خدمة", nameEn = "Catering Service", description = (string?)null,
            categoryCode = "catering", unitOfMeasureCode = "unit", priceAmount = (decimal?)null, currencyCode = (string?)null, attributes = (object?)null,
        });
        var (alreadyInvitedClient, alreadyInvitedSupplierId) = await ActiveSupplierAsync($"AlreadyInvited {Guid.NewGuid():N}"[..30]);
        await alreadyInvitedClient.PostAsJsonAsync("/api/v1/suppliers/me/offerings", new
        {
            nameAr = "خدمة", nameEn = "Catering Service 2", description = (string?)null,
            categoryCode = "catering", unitOfMeasureCode = "unit", priceAmount = (decimal?)null, currencyCode = (string?)null, attributes = (object?)null,
        });
        await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/invitations", new { supplierId = alreadyInvitedSupplierId });
        var (_, noMatchSupplierId) = await ActiveSupplierAsync($"NoMatch {Guid.NewGuid():N}"[..30]);
        _ = noMatchSupplierId;

        var candidates = await officer.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}/invitations/candidates");

        var ids = candidates.EnumerateArray().Select(c => c.GetProperty("supplierId").GetGuid()).ToList();
        ids.Should().Contain(matchSupplierId);
        ids.Should().NotContain(alreadyInvitedSupplierId, "already-invited suppliers are excluded from suggestions");
        ids.Should().NotContain(noMatchSupplierId, "a supplier with no matching-category offering is not suggested");
    }
}
