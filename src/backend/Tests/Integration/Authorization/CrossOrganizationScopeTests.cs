// T1-16 and RISK-004 - cross-tenant data leakage, the risk register's only Critical entry.
//
// These tests exist as a named, findable guard BEFORE the Epics 15-19 dashboards are written, because those
// dashboards are the widest cross-aggregate reads in the product and a scoping mistake there would be
// invisible without them. Some of what they cover overlaps existing per-epic tests; the overlap is
// deliberate - a reviewer looking for "where is cross-tenant isolation proved" should find one file, not five
// scattered assertions. They run in this order: a supplier reading an RFQ they hold no invitation to, a
// supplier reading another supplier's proposal, the supplier RFQ list being filtered rather than merely
// reachable, an evaluator reaching into another RFQ's evaluation, and scoping surviving pagination.
//
// Denial shape is the contract's, not "anything non-200". API-ARCHITECTURE.md's row-scoping section says
// out-of-scope access to an existing resource returns 404 rather than 403 to avoid leaking existence, EXCEPT
// where the persona legitimately shares the collection, and its status table calls 404 "unknown public id, or
// hidden by row-scope (indistinguishable by design)". A supplier does not share the RFQ or proposal
// collection with other suppliers, and an evaluator does not share another RFQ's evaluation, so every case
// here is 404 - and each test asserts that the out-of-scope response says the same thing as the response for
// a reference code that does not exist at all, which is the property "indistinguishable by design" actually
// names. That comparison is over the fields that could reveal existence rather than raw bytes: §7 gives every
// problem+json a traceId and correlationId, random per request, and an instance echoing the caller's OWN
// path, so two responses can never be byte-identical again. None of those three can leak existence - the ids
// are random and the path is the caller's own input - so what must match is everything that describes the
// OUTCOME.
//
// Terminology: the batch brief says "a supplier user from Org A". Suppliers in this domain are scoped by
// SupplierId rather than OrganizationId - Organization is the buyer-side tenant - so these tests exercise
// supplier-to-supplier isolation for the supplier cases and org or assignment isolation for the buyer-side
// and evaluator cases, which is the real boundary the code has.
//
// Each test is written so that a single broken thing cannot make it pass. The no-invitation case would
// require SupplierRfqLoader.LoadInvitedAsync to stop filtering on the caller's own SupplierId AND the
// endpoint to keep returning 404 for a genuinely unknown code - the leak and the control would have to break
// in opposite directions at once, and asserting the two responses are identical is what removes the
// single-break escape.
//
// The other-supplier's-proposal case would require ProposalLoader.LoadAsync to drop its
// "p.SupplierId == scope.SupplierId" predicate while B still happened to have no proposal row of its own, so
// B starts its own proposal first and the assertion is that B sees an EMPTY one rather than A's priced items.
// Both suppliers are invited, which is the harder case: isolation must hold between two legitimately invited
// parties, not merely between an invitee and a stranger.
//
// The list case is the one the batch brief singles out: it seeds an RFQ that WOULD appear if scoping were
// absent. Falsely passing would require SupplierListInvitedRfqsHandler's
// "Invitations.Any(i => i.SupplierId == scope.SupplierId)" predicate to be removed AND the other supplier's
// RFQ to somehow not exist, which the explicit "other supplier can see its own" assertion rules out - the
// negative is only meaningful if B's RFQ is genuinely visible to SOMEONE. The two `data` hops are the only
// change this test has taken: the list returns the documented §5.2 envelope { data, pagination, meta } rather
// than a bare array, so the root is an object. No assertion, control or scoping expectation moved.
//
// The evaluator case would require EvaluationLoader.LoadScopedByAssignmentAsync to stop checking the caller's
// assignment AND RFQ Y to have no evaluation at all, so Y is driven all the way to an open evaluation with
// its own assigned evaluator and that evaluator's successful read is the control. Scoring into another RFQ's
// evaluation is refused on the same boundary rather than merely hidden, and it uses a code that cannot exist
// so the refusal is about the ASSIGNMENT scope rather than the code. The buyer-side evaluation read is a
// permission boundary rather than a scope one: an evaluator holds neither evaluation.open nor
// comparison.view, so the contract's PERMISSION_DENIED 403 applies rather than the existence-hiding 404.
//
// The pagination case is the failure mode pagination introduces: a scoping predicate applied when building
// page one but not re-applied once a cursor narrows the query. A page-one-only assertion cannot see it,
// because the leak appears on page two. Falsely passing would require the same invitation filter to be
// dropped AND supplier B's RFQs not to exist, so B is seeded with MORE RFQs than A at a page size that
// forces A through several pages, and B's own list is asserted non-empty as the control - if scoping were
// lost, B's rows would necessarily surface in A's later pages.
//
// Two helpers arrange time rather than waiting on it. The publish helper uses dates far enough out that the
// six HTTP round-trips inside it cannot overrun them: this raced, because submit-review refuses unless BOTH
// dates are still in the future and the old offsets of +1s and +3s were measured from before those six calls,
// so on a slow runner submit-review arrived after the window had already opened and answered 409. It passed
// locally and on PR #112's own CI, then failed on main, which is what a clock race looks like. The window is
// then moved into the past IN STORAGE rather than waited out: the publish path has already validated real
// future dates, so nothing is being smuggled past a guard, and the test no longer depends on how fast the
// runner is. The transitions themselves still run through the real RfqTimelineJob - only the dates it reads
// are arranged.

namespace MotsSupplierPortal.Tests.Integration.Authorization;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Rfqs;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class CrossOrganizationScopeTests(PostgresApiFixture fixture)
{
    private const string NonExistentReferenceCode = "RFQ-2026-999999";

    private static object RfqBasics(string titleEn, DateTimeOffset opensAt, DateTimeOffset closesAt) => new
    {
        titleAr = "طلب اختبار", titleEn, descriptionAr = (string?)null, descriptionEn = (string?)null,
        currencyCode = "SYP", publishAt = (DateTimeOffset?)null,
        submissionOpensAt = opensAt, submissionClosesAt = closesAt,
        clarificationDeadlineAt = (DateTimeOffset?)null, evaluationTargetDate = (DateTimeOffset?)null,
    };

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

    private async Task RunTimelineJobAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<RfqTimelineJob>().RunAsync(CancellationToken.None);
    }

    private async Task<(string ReferenceCode, Guid ItemId, HttpClient Officer, HttpClient Manager, Guid OrgId)>
        PublishRfqAsync(Guid invitedSupplierId, string titleEn, DateTimeOffset opensAt, DateTimeOffset closesAt)
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var manager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, org.Id);

        var templateResponse = await manager.PostAsJsonAsync("/api/v1/evaluation-templates",
            new { nameAr = "قالب", nameEn = $"Scope Template {Guid.NewGuid():N}" });
        var templateId = (await templateResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await manager.PostAsJsonAsync($"/api/v1/evaluation-templates/{templateId}/criteria", new
        {
            nameAr = "جودة", nameEn = "Quality", dimension = "Technical", weight = 100, maxScore = 100,
            threshold = 50, scoringType = "Numeric", guidanceAr = (string?)null, guidanceEn = (string?)null,
        });
        await manager.PostAsync($"/api/v1/evaluation-templates/{templateId}/activate", null);

        var createResponse = await officer.PostAsJsonAsync("/api/v1/rfqs", RfqBasics(titleEn, opensAt, closesAt));
        var referenceCode = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;

        var itemResponse = await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/items", new
        {
            titleAr = "بند", titleEn = "Item", specificationAr = (string?)null, specificationEn = (string?)null,
            categoryCode = "catering", quantity = 5, unitOfMeasureCode = "unit", isUnitPrice = true, isOptional = false,
        });
        var itemId = (await itemResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();

        await officer.PutAsJsonAsync($"/api/v1/rfqs/{referenceCode}/evaluation-template", new { evaluationTemplateId = templateId });
        await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/invitations", new { supplierId = invitedSupplierId });
        await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/submit-review", null);
        await manager.PostAsync($"/api/v1/rfqs/{referenceCode}/approve", null);
        (await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/publish", null)).EnsureSuccessStatusCode();

        return (referenceCode, itemId, officer, manager, org.Id);
    }

    private static async Task SubmitProposalAsync(HttpClient supplier, string referenceCode, Guid itemId)
    {
        var proposalCode = await supplier.StartProposalAsync(referenceCode);
        await ProposalPatch.PriceItemAsync(supplier, proposalCode, itemId, 5m, 10m, (decimal?)null, 3, (string?)null, (string?)null );
        await ProposalPatch.SetTermsAsync(supplier, proposalCode, new
        {
            currencyCode = "SYP", paymentTerms = "Net 30", incotermCode = "FOB",
            deliveryTermsAr = "٣ أيام", deliveryTermsEn = "3 days", warranty = (string?)null,
            validityStart = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date),
            validityEnd = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date.AddDays(30)),
        });
        (await supplier.PostAsync($"/api/v1/proposals/{proposalCode}/submit", null)).EnsureSuccessStatusCode();
    }

    private static async Task AssertNoExistenceOracleAsync(HttpResponseMessage a, HttpResponseMessage b, string because)
    {
        var left = await a.Content.ReadFromJsonAsync<JsonElement>();
        var right = await b.Content.ReadFromJsonAsync<JsonElement>();

        foreach (var field in new[] { "type", "title", "status", "code", "detail" })
        {
            var inLeft = left.TryGetProperty(field, out var vl) ? vl.ToString() : null;
            var inRight = right.TryGetProperty(field, out var vr) ? vr.ToString() : null;
            inLeft.Should().Be(inRight, $"{because} ('{field}' differs)");
        }
    }

    [Fact]
    public async Task A_supplier_holding_no_invitation_cannot_read_the_rfq_and_cannot_tell_it_exists()
    {
        var (invited, invitedSupplierId) = await ActiveSupplierAsync($"ScopeInv {Guid.NewGuid():N}"[..28]);
        var (outsider, _) = await ActiveSupplierAsync($"ScopeOut {Guid.NewGuid():N}"[..28]);
        var (referenceCode, _, _, _, _) = await PublishRfqAsync(
            invitedSupplierId, "Cross-scope RFQ", DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(8));

        var invitedRead = await invited.GetAsync($"/api/v1/rfqs/{referenceCode}");
        var outsiderRead = await outsider.GetAsync($"/api/v1/rfqs/{referenceCode}");
        var unknownRead = await outsider.GetAsync($"/api/v1/rfqs/{NonExistentReferenceCode}");

        invitedRead.StatusCode.Should().Be(HttpStatusCode.OK, "the actually-invited supplier must still see it");
        outsiderRead.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "API-ARCHITECTURE.md: out-of-scope access to an existing resource returns 404, not 403");
        await AssertNoExistenceOracleAsync(outsiderRead, unknownRead,
            "'hidden by row-scope' and 'unknown public id' must be indistinguishable by design");
    }

    [Fact]
    public async Task A_supplier_cannot_read_another_suppliers_proposal_on_an_rfq_they_are_both_invited_to()
    {
        var (supplierA, supplierAId) = await ActiveSupplierAsync($"ScopePropA {Guid.NewGuid():N}"[..28]);
        var (supplierB, supplierBId) = await ActiveSupplierAsync($"ScopePropB {Guid.NewGuid():N}"[..28]);

        var (referenceCode, itemId, officer, _, _) = await PublishRfqAsync(
            supplierAId, "Cross-scope proposal RFQ", DateTimeOffset.UtcNow.AddMinutes(5), DateTimeOffset.UtcNow.AddDays(8));
        (await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/invitations", new { supplierId = supplierBId }))
            .EnsureSuccessStatusCode();

        await ShiftSubmissionWindowAsync(referenceCode, DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow.AddDays(8));
        await RunTimelineJobAsync();

        await SubmitProposalAsync(supplierA, referenceCode, itemId);

        var bReadsBeforeStarting = await supplierB.GetAsync($"/api/v1/rfqs/{referenceCode}/proposals");
        bReadsBeforeStarting.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "B has not started a proposal - this must be B's own absent state, never A's submitted one");

        var bStart = await supplierB.PostAsync($"/api/v1/rfqs/{referenceCode}/proposals", null);
        bStart.EnsureSuccessStatusCode();
        var bProposal = await bStart.Content.ReadFromJsonAsync<JsonElement>();

        bProposal.GetProperty("items").EnumerateArray().Should().BeEmpty(
            "B's proposal is B's own - it can never contain A's priced items");
        bProposal.GetProperty("state").GetString().Should().Be("Draft");
    }

    [Fact]
    public async Task The_supplier_rfq_list_contains_only_the_callers_own_invitations()
    {
        var (supplierA, supplierAId) = await ActiveSupplierAsync($"ScopeListA {Guid.NewGuid():N}"[..28]);
        var (supplierB, supplierBId) = await ActiveSupplierAsync($"ScopeListB {Guid.NewGuid():N}"[..28]);

        var (aCode, _, _, _, _) = await PublishRfqAsync(
            supplierAId, "List scope A", DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(8));
        var (bCode, _, _, _, _) = await PublishRfqAsync(
            supplierBId, "List scope B", DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(8));

        var aList = await supplierA.GetFromJsonAsync<JsonElement>("/api/v1/rfqs");
        var aCodes = aList.GetProperty("data").EnumerateArray().Select(r => r.GetProperty("rfqCode").GetString()).ToList();

        aCodes.Should().Contain(aCode, "A is invited to its own RFQ");
        aCodes.Should().NotContain(bCode, "B's RFQ exists and would appear here if the list were not invitation-scoped");

        var bList = await supplierB.GetFromJsonAsync<JsonElement>("/api/v1/rfqs");
        bList.GetProperty("data").EnumerateArray().Select(r => r.GetProperty("rfqCode").GetString())
            .Should().Contain(bCode, "control: the seeded RFQ is real and reachable by its own invitee");
    }

    [Fact]
    public async Task An_evaluator_assigned_to_one_rfq_cannot_read_another_rfqs_evaluation_or_scores()
    {
        var (supplierX, supplierXId) = await ActiveSupplierAsync($"ScopeEvX {Guid.NewGuid():N}"[..28]);
        var (supplierY, supplierYId) = await ActiveSupplierAsync($"ScopeEvY {Guid.NewGuid():N}"[..28]);

        var (evaluatorX, evaluatorXId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.Evaluator);
        var (evaluatorY, evaluatorYId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.Evaluator);

        var xCode = await OpenEvaluationAsync(supplierX, supplierXId, "Evaluator scope X", evaluatorXId);
        var yCode = await OpenEvaluationAsync(supplierY, supplierYId, "Evaluator scope Y", evaluatorYId);

        (await evaluatorX.GetAsync($"/api/v1/rfqs/{xCode}/my-evaluation")).StatusCode
            .Should().Be(HttpStatusCode.OK, "control: X's own assignment is readable");
        (await evaluatorY.GetAsync($"/api/v1/rfqs/{yCode}/my-evaluation")).StatusCode
            .Should().Be(HttpStatusCode.OK, "control: Y's evaluation genuinely exists and is readable by its own evaluator");

        var crossRead = await evaluatorX.GetAsync($"/api/v1/rfqs/{yCode}/my-evaluation");
        var unknownRead = await evaluatorX.GetAsync($"/api/v1/rfqs/{NonExistentReferenceCode}/my-evaluation");

        crossRead.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "an evaluator with no assignment on this RFQ is out of scope - 404, not 403");
        await AssertNoExistenceOracleAsync(crossRead, unknownRead,
            "an unassigned evaluation and a non-existent one must be indistinguishable");

        var crossScore = await evaluatorX.PostAsJsonAsync($"/api/v1/rfqs/{yCode}/my-evaluation/scores", new
        { proposalCode = "PRP-2026-999999", criterionId = Guid.CreateVersion7(), rawScore = 90m, commentAr = (string?)null, commentEn = (string?)null });
        crossScore.StatusCode.Should().Be(HttpStatusCode.NotFound, "a write into an unassigned evaluation is refused on the same scope check");

        (await evaluatorX.GetAsync($"/api/v1/rfqs/{yCode}/evaluation")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "evaluator does not hold evaluation.open");
    }

    private async Task<string> OpenEvaluationAsync(HttpClient supplier, Guid supplierId, string titleEn, Guid evaluatorUserId)
    {
        var (referenceCode, itemId, _, manager, _) = await PublishRfqAsync(
            supplierId, titleEn, DateTimeOffset.UtcNow.AddMinutes(5), DateTimeOffset.UtcNow.AddMinutes(10));

        await ShiftSubmissionWindowAsync(referenceCode, DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow.AddMinutes(10));
        await RunTimelineJobAsync();

        await SubmitProposalAsync(supplier, referenceCode, itemId);

        await ShiftSubmissionWindowAsync(referenceCode, DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow.AddSeconds(-1));
        await RunTimelineJobAsync();

        (await manager.PostAsync($"/api/v1/rfqs/{referenceCode}/evaluation/open", null)).EnsureSuccessStatusCode();
        (await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/evaluation/assignments",
            new { evaluatorUserIds = new[] { evaluatorUserId } })).EnsureSuccessStatusCode();

        return referenceCode;
    }

    private async Task ShiftSubmissionWindowAsync(string referenceCode, DateTimeOffset opensAt, DateTimeOffset closesAt)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Rfqs.Where(r => r.ReferenceCode == referenceCode).ExecuteUpdateAsync(p => p
            .SetProperty(r => r.SubmissionOpensAt, opensAt)
            .SetProperty(r => r.SubmissionClosesAt, closesAt));
    }

    [Fact]
    public async Task Supplier_scoping_holds_on_every_page_not_just_the_first()
    {
        var (supplierA, supplierAId) = await ActiveSupplierAsync($"PageScopeA {Guid.NewGuid():N}"[..28]);
        var (supplierB, supplierBId) = await ActiveSupplierAsync($"PageScopeB {Guid.NewGuid():N}"[..28]);

        var aCodes = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var (code, _, _, _, _) = await PublishRfqAsync(
                supplierAId, $"Paged scope A{i}", DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(8));
            aCodes.Add(code);
        }

        var bCodes = new List<string>();
        for (var i = 0; i < 7; i++)
        {
            var (code, _, _, _, _) = await PublishRfqAsync(
                supplierBId, $"Paged scope B{i}", DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(8));
            bCodes.Add(code);
        }

        var seen = new List<string>();
        string? cursor = null;
        var guard = 0;
        do
        {
            var url = cursor is null
                ? "/api/v1/rfqs?pageSize=2"
                : $"/api/v1/rfqs?pageSize=2&cursor={Uri.EscapeDataString(cursor)}";
            var body = await supplierA.GetFromJsonAsync<JsonElement>(url);

            seen.AddRange(body.GetProperty("data").EnumerateArray()
                .Select(r => r.GetProperty("rfqCode").GetString()!));

            var pagination = body.GetProperty("pagination");
            cursor = pagination.GetProperty("hasMore").GetBoolean()
                ? pagination.GetProperty("nextCursor").GetString()
                : null;
        }
        while (cursor is not null && ++guard < 20);

        seen.Should().OnlyHaveUniqueItems("keyset paging must not repeat a row across pages");
        seen.Should().BeEquivalentTo(aCodes, "A sees exactly its own five invitations across all pages");
        foreach (var bCode in bCodes)
        {
            seen.Should().NotContain(bCode, "B's RFQ must not surface on ANY of A's pages, including after the cursor");
        }

        var bFirstPage = await supplierB.GetFromJsonAsync<JsonElement>("/api/v1/rfqs?pageSize=100");
        bFirstPage.GetProperty("data").EnumerateArray()
            .Select(r => r.GetProperty("rfqCode").GetString()).Should().BeEquivalentTo(bCodes);
    }
}
