using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// §12-A/Part B: the allow-list gate on the converged <c>/api/v1/rfqs</c> routes.
///
/// <para><b>What this exists to stop.</b> Before convergence, a supplier and a buyer reached two
/// different endpoints backed by two different handlers and two different DTOs, so emitting a
/// buyer-only field to a supplier was structurally impossible - <c>SupplierRfqDto</c> has no
/// <c>invitations</c> member to populate. §12.4 requires one route serving both
/// (*"Fields visible per persona are row-scoped"*, *"- for buyers - invitations[]"*), and the
/// implementation keeps the two handlers precisely to preserve that property. But the ROUTE is now
/// shared, and the thing standing between the two shapes is a single dispatch on
/// <c>scope.SupplierId</c>.</para>
///
/// <para><b>The failure mode is not that dispatch breaking.</b> It is a field added to the buyer
/// DTO months from now by someone who does not know the route is persona-shaped, defaulting to
/// "include it" because that is what the buyer needed. Nothing in the type system objects. So this
/// asserts the supplier response's key set EXACTLY, in both directions - an unexpected key fails,
/// and a key that disappears fails too, so the list cannot rot into a description of a response
/// that no longer exists. Same shape as the T2-33 enum-coverage test.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class RfqPersonaShapeTests(PostgresApiFixture fixture)
{
    /// <summary>
    /// Every top-level key a supplier may receive from <c>GET /rfqs/{rfqCode}</c>. Derived from
    /// <c>SupplierRfqDto</c>, which is deliberately narrower than the buyer's <c>RfqDto</c>.
    /// Changing this list is a decision about what suppliers can see, which is the point of making
    /// it explicit rather than inferred.
    /// </summary>
    private static readonly string[] SupplierDetailKeys =
    [
        "referenceCode", "titleAr", "titleEn", "descriptionAr", "descriptionEn", "currencyCode",
        "state", "submissionOpensAt", "submissionClosesAt", "clarificationDeadlineAt",
        "items", "requirements", "attachments", "myInvitationStatus", "clarifications", "addenda",
    ];

    /// <summary>Every top-level key a supplier may receive from a row of <c>GET /rfqs</c>.</summary>
    private static readonly string[] SupplierListItemKeys =
    [
        "referenceCode", "titleAr", "titleEn", "state", "myInvitationStatus", "createdAt",
        // §12-A/D: §12.4's documented list fields. Each is here because a supplier is documented to
        // receive it - and each is asserted absent from the buyer row below, because §12.4
        // documents only the supplier shape and hasDraftProposal/myInvitationStatus are
        // caller-relative.
        "publishedAt", "buyingOrg", "itemsCount", "hasDraftProposal",
    ];

    /// <summary>
    /// §12.4 names these as buyer-side. Asserted by NAME as well as by the exact-set check above,
    /// because a reader of a failure should see which concept leaked, not only that the key count
    /// moved.
    /// </summary>
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

    // ---- detail ------------------------------------------------------------------------------

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

    /// <summary>
    /// The control. Without it, every assertion above would also pass if the buyer branch silently
    /// stopped returning its own fields - the supplier shape would be "clean" because nothing was
    /// there to leak.
    /// </summary>
    [Fact]
    public async Task The_buyer_detail_does_carry_the_buyer_only_keys()
    {
        var (_, officer, code) = await PublishedRfqAsync();

        var body = await officer.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{code}");

        body.TryGetProperty("invitations", out _).Should().BeTrue("§12.4: \"- for buyers - invitations[]\"");
        body.TryGetProperty("approvals", out _).Should().BeTrue();
        body.TryGetProperty("organizationId", out _).Should().BeTrue();
    }

    // ---- list --------------------------------------------------------------------------------

    [Fact]
    public async Task The_supplier_list_row_carries_exactly_the_allow_listed_keys_and_no_others()
    {
        var (supplier, _, code) = await PublishedRfqAsync();

        var body = await supplier.GetFromJsonAsync<JsonElement>("/api/v1/rfqs?pageSize=100");
        var row = body.GetProperty("data").EnumerateArray()
            .Single(r => r.GetProperty("referenceCode").GetString() == code);

        KeysOf(row).Should().BeEquivalentTo(SupplierListItemKeys);
    }

    /// <summary>
    /// The list is where the two personas differ MOST: §12.4 documents only the supplier's shape,
    /// and what a buyer receives is an invention (reported as such). This pins that the buyer's row
    /// does not carry the supplier's caller-relative field, which would be meaningless - and false -
    /// for a buyer.
    /// </summary>
    [Fact]
    public async Task The_buyer_list_row_carries_no_caller_relative_supplier_field()
    {
        var (_, officer, code) = await PublishedRfqAsync();

        var body = await officer.GetFromJsonAsync<JsonElement>("/api/v1/rfqs?pageSize=100");
        var row = body.GetProperty("data").EnumerateArray()
            .Single(r => r.GetProperty("referenceCode").GetString() == code);

        foreach (var callerRelative in new[] { "myInvitationStatus", "hasDraftProposal" })
        {
            row.TryGetProperty(callerRelative, out _).Should().BeFalse(
                $"'{callerRelative}' is relative to the CALLING SUPPLIER (§12.4 says so of " +
                "invitationStatus in as many words), so emitting it to a buyer would be a lie rather " +
                "than merely a redundant field");
        }
    }

    // ---- ministry_viewer ----------------------------------------------------------------------

    /// <summary>
    /// MSP-62 / BRULE-086: ministry_viewer's grant is deliberately EMPTY - the Ministry's access is
    /// *"read-only, cross-organization aggregate access"*, and OQ-001 (whether line-level access is
    /// permitted) is unanswered. Convergence puts both personas on one route, so the risk is that a
    /// third persona quietly acquires access to it. It must receive nothing from either route.
    /// </summary>
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
