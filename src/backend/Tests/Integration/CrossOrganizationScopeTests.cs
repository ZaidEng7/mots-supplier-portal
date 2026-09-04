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

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// T1-16 / RISK-004 (cross-tenant data leakage - the risk register's only Critical entry).
///
/// <para>These four tests exist as a named, findable guard BEFORE the Epics 15-19 dashboards are
/// written, because those dashboards are the widest cross-aggregate reads in the product and a
/// scoping mistake there would be invisible without them. Some of what they cover overlaps existing
/// per-epic tests; the overlap is deliberate - a reviewer looking for "where is cross-tenant
/// isolation proved" should find one file, not five scattered assertions.</para>
///
/// <para><b>Denial shape is the contract's, not "anything non-200".</b> API-ARCHITECTURE.md §
/// row-scoping: <i>"Out-of-scope access to an existing resource returns <b>404</b> (not 403) to
/// avoid leaking existence, <b>except</b> where the persona legitimately shares the collection
/// (then 403 with OUT_OF_SCOPE)."</i> and its status table: <i>"404 Not Found | Unknown public id,
/// or hidden by row-scope (indistinguishable by design)"</i>. A supplier does not share the RFQ or
/// proposal collection with other suppliers, and an evaluator does not share another RFQ's
/// evaluation, so every case here is 404 - and each test asserts that the out-of-scope response is
/// byte-identical to the response for a reference code that does not exist at all, which is the
/// property "indistinguishable by design" actually names.</para>
///
/// <para><b>Terminology note.</b> The batch brief says "a supplier user from Org A". Suppliers in
/// this domain are scoped by <c>SupplierId</c>, not <c>OrganizationId</c> - Organization is the
/// buyer-side tenant. These tests therefore exercise supplier-to-supplier isolation for the
/// supplier cases and org/assignment isolation for the buyer-side and evaluator cases, which is the
/// real boundary the code has.</para>
/// </summary>
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

    /// <summary>Publishes an RFQ in its own Organization, inviting exactly one supplier.</summary>
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

    /// <summary>
    /// Compares the fields that could reveal existence, rather than raw bytes.
    ///
    /// <para>§7 gives every problem+json a <c>traceId</c> and <c>correlationId</c> (random per
    /// request) and an <c>instance</c> echoing the caller's OWN path, so two responses can never be
    /// byte-identical again. None of the three can leak existence - the ids are random and the path
    /// is the caller's own input. What must match is everything that describes the OUTCOME.</para>
    /// </summary>
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

    // ---- 1. Supplier requests an RFQ they hold no invitation to -------------------------------

    /// <summary>
    /// Falsely passing would require: <c>SupplierRfqLoader.LoadInvitedAsync</c> to stop filtering on
    /// the caller's own <c>SupplierId</c> AND the endpoint to keep returning 404 for a genuinely
    /// unknown code - i.e. the leak and the control would have to break in opposite directions at
    /// once. Asserting the two responses are identical is what removes the single-break escape.
    /// </summary>
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

    // ---- 2. Supplier requests another supplier's proposal --------------------------------------

    /// <summary>
    /// Falsely passing would require <c>ProposalLoader.LoadAsync</c> to drop its
    /// <c>p.SupplierId == scope.SupplierId</c> predicate while B still happened to have no proposal
    /// row of its own - so the assertion deliberately has B start its own proposal first and checks
    /// B sees an EMPTY one rather than A's priced items.
    /// </summary>
    [Fact]
    public async Task A_supplier_cannot_read_another_suppliers_proposal_on_an_rfq_they_are_both_invited_to()
    {
        var (supplierA, supplierAId) = await ActiveSupplierAsync($"ScopePropA {Guid.NewGuid():N}"[..28]);
        var (supplierB, supplierBId) = await ActiveSupplierAsync($"ScopePropB {Guid.NewGuid():N}"[..28]);

        var (referenceCode, itemId, officer, _, _) = await PublishRfqAsync(
            supplierAId, "Cross-scope proposal RFQ", DateTimeOffset.UtcNow.AddSeconds(1), DateTimeOffset.UtcNow.AddDays(8));
        // Both suppliers are invited: this is the harder case. Isolation must hold between two
        // legitimately-invited parties, not merely between an invitee and a stranger.
        (await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/invitations", new { supplierId = supplierBId }))
            .EnsureSuccessStatusCode();

        await Task.Delay(TimeSpan.FromSeconds(1.2));
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

    // ---- 3. The supplier RFQ list is filtered, not merely reachable ----------------------------

    /// <summary>
    /// This is the test the batch brief singles out: it seeds an RFQ that WOULD appear if scoping
    /// were absent. Falsely passing would require
    /// <c>SupplierListInvitedRfqsHandler</c>'s <c>Invitations.Any(i =&gt; i.SupplierId == scope.SupplierId)</c>
    /// predicate to be removed AND the other supplier's RFQ to somehow not exist - which the
    /// explicit "other supplier can see its own" assertion rules out.
    /// </summary>
    [Fact]
    public async Task The_supplier_rfq_list_contains_only_the_callers_own_invitations()
    {
        var (supplierA, supplierAId) = await ActiveSupplierAsync($"ScopeListA {Guid.NewGuid():N}"[..28]);
        var (supplierB, supplierBId) = await ActiveSupplierAsync($"ScopeListB {Guid.NewGuid():N}"[..28]);

        var (aCode, _, _, _, _) = await PublishRfqAsync(
            supplierAId, "List scope A", DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(8));
        var (bCode, _, _, _, _) = await PublishRfqAsync(
            supplierBId, "List scope B", DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(8));

        // The two `GetProperty("data")` hops below are the ONLY change to this test: the list now
        // returns the documented §5.2 envelope `{ data, pagination, meta }` instead of a bare array,
        // so the root is an object. No assertion, control, or scoping expectation moved.
        var aList = await supplierA.GetFromJsonAsync<JsonElement>("/api/v1/rfqs");
        var aCodes = aList.GetProperty("data").EnumerateArray().Select(r => r.GetProperty("rfqCode").GetString()).ToList();

        aCodes.Should().Contain(aCode, "A is invited to its own RFQ");
        aCodes.Should().NotContain(bCode, "B's RFQ exists and would appear here if the list were not invitation-scoped");

        // The negative is only meaningful if B's RFQ is genuinely visible to SOMEONE.
        var bList = await supplierB.GetFromJsonAsync<JsonElement>("/api/v1/rfqs");
        bList.GetProperty("data").EnumerateArray().Select(r => r.GetProperty("rfqCode").GetString())
            .Should().Contain(bCode, "control: the seeded RFQ is real and reachable by its own invitee");
    }

    // ---- 4. Evaluator assigned to RFQ X reaches into RFQ Y -------------------------------------

    /// <summary>
    /// Falsely passing would require <c>EvaluationLoader.LoadScopedByAssignmentAsync</c> to stop
    /// checking the caller's assignment AND RFQ Y to have no evaluation at all - so Y is driven all
    /// the way to an open evaluation with its own assigned evaluator, and that evaluator's
    /// successful read is asserted as the control.
    /// </summary>
    [Fact]
    public async Task An_evaluator_assigned_to_one_rfq_cannot_read_another_rfqs_evaluation_or_scores()
    {
        var (supplierX, supplierXId) = await ActiveSupplierAsync($"ScopeEvX {Guid.NewGuid():N}"[..28]);
        var (supplierY, supplierYId) = await ActiveSupplierAsync($"ScopeEvY {Guid.NewGuid():N}"[..28]);

        var (evaluatorX, evaluatorXId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.Evaluator);
        var (evaluatorY, evaluatorYId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.Evaluator);

        var xCode = await OpenEvaluationAsync(supplierX, supplierXId, "Evaluator scope X", evaluatorXId);
        var yCode = await OpenEvaluationAsync(supplierY, supplierYId, "Evaluator scope Y", evaluatorYId);

        // Control: each evaluator can read the evaluation they ARE assigned to.
        (await evaluatorX.GetAsync($"/api/v1/rfqs/{xCode}/my-evaluation")).StatusCode
            .Should().Be(HttpStatusCode.OK, "control: X's own assignment is readable");
        (await evaluatorY.GetAsync($"/api/v1/rfqs/{yCode}/my-evaluation")).StatusCode
            .Should().Be(HttpStatusCode.OK, "control: Y's evaluation genuinely exists and is readable by its own evaluator");

        // The boundary: X reaching into Y.
        var crossRead = await evaluatorX.GetAsync($"/api/v1/rfqs/{yCode}/my-evaluation");
        var unknownRead = await evaluatorX.GetAsync($"/api/v1/rfqs/{NonExistentReferenceCode}/my-evaluation");

        crossRead.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "an evaluator with no assignment on this RFQ is out of scope - 404, not 403");
        await AssertNoExistenceOracleAsync(crossRead, unknownRead,
            "an unassigned evaluation and a non-existent one must be indistinguishable");

        // Scoring into another RFQ's evaluation is refused on the same boundary, not merely hidden.
        var crossScore = await evaluatorX.PostAsJsonAsync($"/api/v1/rfqs/{yCode}/my-evaluation/scores", new
        // A code that cannot exist, so the refusal is about the ASSIGNMENT scope rather than the code.
        { proposalCode = "PRP-2026-999999", criterionId = Guid.CreateVersion7(), rawScore = 90m, commentAr = (string?)null, commentEn = (string?)null });
        crossScore.StatusCode.Should().Be(HttpStatusCode.NotFound, "a write into an unassigned evaluation is refused on the same scope check");

        // The buyer-side evaluation read is a permission boundary rather than a scope one: an
        // evaluator holds neither evaluation.open nor comparison.view, so the contract's
        // PERMISSION_DENIED (403) applies rather than the existence-hiding 404.
        (await evaluatorX.GetAsync($"/api/v1/rfqs/{yCode}/evaluation")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "evaluator does not hold evaluation.open");
    }

    /// <summary>Drives one RFQ from creation to an open evaluation with <paramref name="evaluatorUserId"/> assigned.</summary>
    private async Task<string> OpenEvaluationAsync(HttpClient supplier, Guid supplierId, string titleEn, Guid evaluatorUserId)
    {
        var (referenceCode, itemId, _, manager, _) = await PublishRfqAsync(
            supplierId, titleEn, DateTimeOffset.UtcNow.AddSeconds(1), DateTimeOffset.UtcNow.AddSeconds(3));

        await Task.Delay(TimeSpan.FromSeconds(1.2));
        await RunTimelineJobAsync();

        await SubmitProposalAsync(supplier, referenceCode, itemId);

        await Task.Delay(TimeSpan.FromSeconds(2));
        await RunTimelineJobAsync();

        (await manager.PostAsync($"/api/v1/rfqs/{referenceCode}/evaluation/open", null)).EnsureSuccessStatusCode();
        (await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/evaluation/assignments",
            new { evaluatorUserIds = new[] { evaluatorUserId } })).EnsureSuccessStatusCode();

        return referenceCode;
    }

    // ---- 5. Scoping must survive pagination (T2 Item 3) ----------------------------------------

    /// <summary>
    /// The failure mode pagination introduces: a scoping predicate applied when building page one
    /// but not re-applied once a cursor narrows the query. A page-one-only assertion cannot see it -
    /// the leak appears on page two.
    ///
    /// <para>Falsely passing would require `SupplierListInvitedRfqsHandler`'s
    /// <c>Invitations.Any(i =&gt; i.SupplierId == supplierId)</c> filter to be dropped AND supplier B's
    /// RFQs not to exist. B is therefore seeded with MORE RFQs than A, at a page size that forces A
    /// through several pages, and B's own list is asserted non-empty as the control - so if scoping
    /// were lost, B's rows would necessarily surface in A's later pages.</para>
    /// </summary>
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

        // Control: B's rows are real and reachable by B, so the negative above is about scoping.
        var bFirstPage = await supplierB.GetFromJsonAsync<JsonElement>("/api/v1/rfqs?pageSize=100");
        bFirstPage.GetProperty("data").EnumerateArray()
            .Select(r => r.GetProperty("rfqCode").GetString()).Should().BeEquivalentTo(bCodes);
    }
}
