// One tender route serves a buyer and a bidder, and the bidder's response shape is pinned exactly.
//
//
// WHAT THIS EXISTS TO STOP
//
// Before the routes converged, a supplier and a buyer reached two different endpoints backed by two different
// handlers and two different read models, so emitting a buyer-only field to a supplier was structurally
// impossible: the supplier's shape has no member to populate.
//
// The written contract requires one route serving both, and the implementation keeps the two handlers precisely to
// preserve that property. But the ROUTE is now shared, and the thing standing between the two shapes is a single
// dispatch on whether the caller has a company.
//
// The failure mode is not that dispatch breaking. It is a field added to the buyer's shape months from now by
// somebody who does not know the route is persona-shaped, defaulting to "include it" because that is what the
// buyer needed. Nothing in the type system objects.
//
// So the supplier's key set is asserted EXACTLY, in both directions: an unexpected key fails, and a key that
// disappears fails too, so the list cannot rot into a description of a response that no longer exists.
//
// The buyer-side concepts are also asserted BY NAME as well as by the exact-set check, because a reader of a
// failure should see which concept leaked rather than only that the key count moved.
//
//
// THE ALLOW-LIST IS A DECISION, WHICH IS WHY IT IS EXPLICIT
//
// Changing it is a decision about what suppliers can see.
//
// It has moved twice, deliberately. Once when the supplier-facing shapes adopted the contract's own vocabulary,
// because the list moves with them. And once when a deadline field appeared, which is exactly the gate doing its
// job: it failed when the field arrived, and adding it was somebody deciding it should be there.
//
// The deadline's reason and its timestamp are on the list for a stated reason: the notification cannot carry the
// reason, because the payload allow-list already refused a date as content, so the supplier reads it here beside
// the deadline it explains. Both are the buyer's own words about the buyer's own action, and neither says anything
// about another bidder, which is what this list exists to prevent leaking.
//
//
// THE CONTROL, AND ONE DELIBERATE SEAM
//
// Without the buyer-side control, every assertion would also pass if the buyer branch silently stopped returning
// its own fields, because the supplier shape would look clean when there was nothing to leak.
//
// The list is where the two personas differ most: the contract documents only the supplier's shape, and what a
// buyer receives is an invention, reported as such. So the buyer's row is pinned as NOT carrying the supplier's
// caller-relative field, which would be meaningless and false for a buyer.
//
// The buyer's row also keeps its older field name, because the conformance work covered the supplier-facing shapes
// the contract specifies and the buyer's row is field-specified nowhere. Renaming it would have been inventing
// conformance rather than applying it. This test is where that seam is visible, and it is deliberate.
//
//
// AND A THIRD PERSONA MUST RECEIVE NOTHING
//
// The ministry viewer's grant is deliberately empty: its access is read-only cross-organization aggregate access,
// and whether line-level access is permitted is an unanswered question.
//
// Convergence puts both personas on one route, so the risk is that a third quietly acquires access to it.

namespace MotsSupplierPortal.Tests.Integration.Rfqs;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class RfqPersonaShapeTests(PostgresApiFixture fixture)
{
    private static readonly string[] SupplierDetailKeys =
    [
        "rfqCode", "titleAr", "titleEn", "descriptionAr", "descriptionEn", "currencyCode",
        "state", "submissionOpensAt", "submissionDeadline", "clarificationDeadlineAt",
        "items", "requirements", "attachments", "invitationStatus", "clarifications", "addenda",
        "submissionDeadlineChangeReason", "submissionDeadlineChangedAt",
    ];

    private static readonly string[] SupplierListItemKeys =
    [
        "rfqCode", "titleAr", "titleEn", "state", "invitationStatus", "createdAt",
        "publishedAt", "buyingOrg", "itemsCount", "hasDraftProposal",
        "submissionDeadline",
    ];

    private static readonly string[] BuyerOnlyKeys = ["invitations", "approvals", "evaluationTemplateId", "evaluationTemplateVersion", "organizationId"];

    private async Task<(HttpClient Supplier, HttpClient Officer, string ReferenceCode)> PublishedRfqAsync()
    {
        var name = $"Shape {Guid.NewGuid():N}"[..24];
        var (supplier, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, name);

        Guid supplierId;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Suppliers.FirstAsync(s => s.DisplayNameEn == name);
            supplierId = row.Id;
            await db.Suppliers.Where(s => s.Id == supplierId).ExecuteUpdateAsync(p => p
                .SetProperty(s => s.OnboardingState, SupplierOnboardingState.Approved)
                .SetProperty(s => s.LifecycleState, SupplierLifecycleState.Active));
        }

        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var manager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, org.Id);

        var templateResponse = await manager.PostAsJsonAsync("/api/v1/evaluation-templates",
            new { nameAr = "قالب", nameEn = $"Shape Template {Guid.NewGuid():N}" });
        var templateId = (await templateResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await manager.PostAsJsonAsync($"/api/v1/evaluation-templates/{templateId}/criteria", new
        {
            nameAr = "جودة", nameEn = "Quality", dimension = "Technical", weight = 100, maxScore = 100,
            threshold = 50, scoringType = "Numeric", guidanceAr = (string?)null, guidanceEn = (string?)null,
        });
        await manager.PostAsync($"/api/v1/evaluation-templates/{templateId}/activate", null);

        var created = await officer.PostAsJsonAsync("/api/v1/rfqs", new
        {
            titleAr = "طلب", titleEn = "Persona Shape RFQ", descriptionAr = (string?)null, descriptionEn = (string?)null,
            currencyCode = "SYP", publishAt = (DateTimeOffset?)null,
            submissionOpensAt = DateTimeOffset.UtcNow.AddDays(1), submissionClosesAt = DateTimeOffset.UtcNow.AddDays(8),
            clarificationDeadlineAt = (DateTimeOffset?)null, evaluationTargetDate = (DateTimeOffset?)null,
        });
        var code = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;

        await officer.PostAsJsonAsync($"/api/v1/rfqs/{code}/items", new
        {
            titleAr = "بند", titleEn = "Item", specificationAr = (string?)null, specificationEn = (string?)null,
            categoryCode = "catering", quantity = 5, unitOfMeasureCode = "unit", isUnitPrice = true, isOptional = false,
        });
        await officer.PutAsJsonAsync($"/api/v1/rfqs/{code}/evaluation-template", new { evaluationTemplateId = templateId });
        await officer.PostAsJsonAsync($"/api/v1/rfqs/{code}/invitations", new { supplierId });
        await officer.PostAsync($"/api/v1/rfqs/{code}/submit-review", null);
        await manager.PostAsync($"/api/v1/rfqs/{code}/approve", null);
        (await officer.PostAsync($"/api/v1/rfqs/{code}/publish", null)).EnsureSuccessStatusCode();

        return (supplier, officer, code);
    }

    private static IEnumerable<string> KeysOf(JsonElement obj) => obj.EnumerateObject().Select(p => p.Name);

    [Fact]
    public async Task The_supplier_detail_carries_exactly_the_allow_listed_keys_and_no_others()
    {
        var (supplier, _, code) = await PublishedRfqAsync();

        var body = await supplier.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{code}");
        var keys = KeysOf(body).ToArray();

        keys.Should().BeEquivalentTo(SupplierDetailKeys,
            "the supplier shape is an allow-list, not a default: an EXTRA key here is a field that " +
            "leaked from the buyer branch, and a MISSING key means this list now describes a " +
            "response that no longer exists");
    }

    [Fact]
    public async Task The_supplier_detail_carries_no_buyer_only_concept()
    {
        var (supplier, _, code) = await PublishedRfqAsync();

        var body = await supplier.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{code}");

        foreach (var buyerKey in BuyerOnlyKeys)
        {
            body.TryGetProperty(buyerKey, out _).Should().BeFalse(
                $"'{buyerKey}' is buyer-side per §12.4 (\"- for buyers - invitations[]\", and " +
                "\"a supplier never sees other suppliers' proposals or the evaluation internals\")");
        }
    }

    [Fact]
    public async Task The_buyer_detail_does_carry_the_buyer_only_keys()
    {
        var (_, officer, code) = await PublishedRfqAsync();

        var body = await officer.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{code}");

        body.TryGetProperty("invitations", out _).Should().BeTrue("§12.4: \"- for buyers - invitations[]\"");
        body.TryGetProperty("approvals", out _).Should().BeTrue();
        body.TryGetProperty("organizationId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task The_supplier_list_row_carries_exactly_the_allow_listed_keys_and_no_others()
    {
        var (supplier, _, code) = await PublishedRfqAsync();

        var body = await supplier.GetFromJsonAsync<JsonElement>("/api/v1/rfqs?pageSize=100");
        var row = body.GetProperty("data").EnumerateArray()
            .Single(r => r.GetProperty("rfqCode").GetString() == code);

        KeysOf(row).Should().BeEquivalentTo(SupplierListItemKeys);
    }

    [Fact]
    public async Task The_buyer_list_row_carries_no_caller_relative_supplier_field()
    {
        var (_, officer, code) = await PublishedRfqAsync();

        var body = await officer.GetFromJsonAsync<JsonElement>("/api/v1/rfqs?pageSize=100");
        var row = body.GetProperty("data").EnumerateArray()
            .Single(r => r.GetProperty("referenceCode").GetString() == code);

        foreach (var callerRelative in new[] { "invitationStatus", "hasDraftProposal" })
        {
            row.TryGetProperty(callerRelative, out _).Should().BeFalse(
                $"'{callerRelative}' is relative to the CALLING SUPPLIER (§12.4 says so of " +
                "invitationStatus in as many words), so emitting it to a buyer would be a lie rather " +
                "than merely a redundant field");
        }
    }

    [Theory]
    [InlineData("/api/v1/rfqs")]
    [InlineData("/api/v1/rfqs/RFQ-2026-000001")]
    public async Task A_ministry_viewer_receives_nothing_from_the_converged_routes(string path)
    {
        var ministry = await StaffTestClient.CreateAsync(fixture, Roles.MinistryViewer, organizationId: null);

        var response = await ministry.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "ministry_viewer holds no rfq.read, and OQ-001 has not granted it line-level access");
    }
}
