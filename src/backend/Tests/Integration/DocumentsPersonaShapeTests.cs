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
/// The allow-list gate for <c>GET /suppliers/{supplierCode}/documents</c>, which serves two
/// personas from one path: the owner gets their onboarding checklist, a reviewer gets §12.3's paged
/// back-office grid.
///
/// <para><b>Why this gate exists, in the same words as RfqPersonaShapeTests.</b> Persona dispatch
/// replaces a structural guarantee with a runtime branch. Two separate routes could not leak into
/// each other; one route with a branch can, and the failure is not the branch breaking - it is a
/// field added months from now by someone who does not know the response is persona-shaped,
/// defaulting to "include it" because that is what the reviewer needed. Nothing in the type system
/// objects. So both shapes are pinned EXACTLY, in both directions: an extra key fails, and a key
/// the response no longer has fails too, so the list cannot rot into a description of something
/// that no longer exists.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class DocumentsPersonaShapeTests(PostgresApiFixture fixture)
{
    /// <summary>Every top-level key on a row of the OWNER's checklist (DocumentTypeStatusDto).</summary>
    private static readonly string[] OwnerChecklistKeys =
    [
        "documentTypeId", "code", "nameAr", "nameEn", "isRequired", "expiryTracked", "latestDocument",
    ];

    /// <summary>Every top-level key on a row of the REVIEWER's §12.3 grid (SupplierDocumentListItemDto).</summary>
    private static readonly string[] ReviewerRowKeys =
    [
        "documentId", "documentTypeCode", "state", "expiresAt", "expiryState", "downloadUrl", "uploadedAt",
    ];

    /// <summary>
    /// Concepts the reviewer's grid exposes that the owner's checklist deliberately does not.
    /// Asserted by NAME as well as by the exact-set check, so a failure says which concept leaked
    /// rather than printing a key diff and leaving the reader to work it out.
    /// </summary>
    private static readonly string[] ReviewerOnlyKeys = ["documentId", "downloadUrl", "expiryState", "uploadedAt"];

    /// <summary>The owner's checklist is a bare array; the reviewer's grid is the §5.2 envelope.</summary>
    private static readonly string[] EnvelopeKeys = ["data", "pagination", "meta"];

    private async Task<(HttpClient Owner, HttpClient Reviewer, string Code)> SeededAsync()
    {
        var name = $"DocShape {Guid.NewGuid():N}"[..26];
        var (owner, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, name);

        string code;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var supplier = await db.Suppliers.FirstAsync(s => s.DisplayNameEn == name);
            code = supplier.ReferenceCode;
            var type = await db.DocumentTypes.Where(t => t.IsActive && !t.ExpiryTracked).FirstAsync();

            db.SupplierDocuments.Add(SupplierDocument.CreatePendingScan(
                supplier.Id, type.Id, version: 1, quarantineKey: $"shape/{Guid.NewGuid():N}",
                originalFileName: "shape.pdf", contentType: "application/pdf", sizeBytes: 512,
                uploadedByUserId: Guid.CreateVersion7(), issueDate: null, expiryDate: null,
                expiryTracked: false, today: DateOnly.FromDateTime(DateTime.UtcNow)));
            await db.SaveChangesAsync();
        }

        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer, organizationId: null);
        return (owner, reviewer, code);
    }

    private static IEnumerable<string> KeysOf(JsonElement obj) => obj.EnumerateObject().Select(p => p.Name);

    // ---- the owner's shape --------------------------------------------------------------------

    [Fact]
    public async Task The_owner_checklist_row_carries_exactly_the_allow_listed_keys()
    {
        var (owner, _, code) = await SeededAsync();

        var body = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/suppliers/{code}/documents");

        body.ValueKind.Should().Be(JsonValueKind.Array, "the owner's view is the checklist, not a paged envelope");
        var row = body.EnumerateArray().First();
        KeysOf(row).Should().BeEquivalentTo(OwnerChecklistKeys,
            "the checklist shape is an allow-list, not a default: an EXTRA key is a field that leaked " +
            "from the reviewer branch, and a MISSING key means this list describes a response that " +
            "no longer exists");
    }

    [Fact]
    public async Task The_owner_checklist_carries_no_reviewer_only_concept()
    {
        var (owner, _, code) = await SeededAsync();

        var body = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/suppliers/{code}/documents");
        var row = body.EnumerateArray().First();

        foreach (var reviewerKey in ReviewerOnlyKeys)
        {
            row.TryGetProperty(reviewerKey, out _).Should().BeFalse(
                $"'{reviewerKey}' belongs to the back-office grid (§12.3), not to the supplier's own checklist");
        }

        // And the envelope itself must not appear: the checklist is not paginated, so emitting
        // pagination/meta here would be the reviewer's response shape reaching the wrong persona.
        foreach (var envelopeKey in EnvelopeKeys)
        {
            row.TryGetProperty(envelopeKey, out _).Should().BeFalse();
        }
    }

    // ---- the reviewer's shape -----------------------------------------------------------------

    [Fact]
    public async Task The_reviewer_row_carries_exactly_the_allow_listed_keys()
    {
        var (_, reviewer, code) = await SeededAsync();

        var body = await reviewer.GetFromJsonAsync<JsonElement>($"/api/v1/suppliers/{code}/documents");

        KeysOf(body).Should().BeEquivalentTo(EnvelopeKeys, "the reviewer's view is the §5.2 envelope");
        var row = body.GetProperty("data").EnumerateArray().First();
        KeysOf(row).Should().BeEquivalentTo(ReviewerRowKeys);
    }

    /// <summary>
    /// The control. Without it every negative above would also pass if the reviewer branch quietly
    /// stopped returning its own fields - the owner's shape would look "clean" because there was
    /// nothing to leak. This is the same trap the owner control caught for
    /// <c>GET /proposals/{proposalCode}</c>, where a cross-org negative was passing against an
    /// unrouted 404.
    /// </summary>
    [Fact]
    public async Task The_reviewer_row_does_carry_the_reviewer_only_keys()
    {
        var (_, reviewer, code) = await SeededAsync();

        var body = await reviewer.GetFromJsonAsync<JsonElement>($"/api/v1/suppliers/{code}/documents");
        var row = body.GetProperty("data").EnumerateArray().First();

        foreach (var reviewerKey in ReviewerOnlyKeys)
        {
            row.TryGetProperty(reviewerKey, out _).Should().BeTrue(
                $"'{reviewerKey}' is documented in §12.3's worked response and must actually be there, " +
                "or the negatives above are asserting against an empty shape");
        }
    }

    /// <summary>
    /// The checklist carries a field the grid does not - proof the two shapes are genuinely
    /// different rather than one being a subset that happens to pass both allow-lists.
    /// </summary>
    [Fact]
    public async Task The_reviewer_row_carries_no_checklist_only_concept()
    {
        var (_, reviewer, code) = await SeededAsync();

        var body = await reviewer.GetFromJsonAsync<JsonElement>($"/api/v1/suppliers/{code}/documents");
        var row = body.GetProperty("data").EnumerateArray().First();

        foreach (var checklistKey in new[] { "isRequired", "expiryTracked", "latestDocument", "nameAr", "nameEn" })
        {
            row.TryGetProperty(checklistKey, out _).Should().BeFalse(
                $"'{checklistKey}' is a document-TYPE concept from the onboarding checklist; the grid " +
                "lists documents, not the catalogue of types a supplier owes");
        }
    }
}
