// One document route serves two personas, and both response shapes are pinned exactly.
//
// The owner gets their onboarding checklist. A reviewer gets the paged back-office grid.
//
//
// WHY THIS GATE EXISTS
//
// Persona dispatch replaces a structural guarantee with a runtime branch. Two separate routes could not leak into
// each other; one route with a branch can.
//
// And the failure is not the branch breaking. It is a field added months from now by somebody who does not know
// the response is persona-shaped, defaulting to "include it" because that is what the reviewer needed. Nothing in
// the type system objects.
//
// So both shapes are pinned in BOTH directions: an extra key fails, and a key the response no longer has fails
// too, so the list cannot rot into a description of something that no longer exists.
//
// The concepts the reviewer's grid exposes and the owner's checklist does not are also asserted BY NAME as well
// as by the exact-set check, so a failure says which concept leaked rather than printing a key difference and
// leaving the reader to work it out.
//
// The envelope itself must not appear on the owner's side: the checklist is not paged, so emitting paging
// metadata there would be the reviewer's response shape reaching the wrong persona.
//
//
// THE CONTROLS
//
// Without the reviewer-side control, every negative above would also pass if the reviewer branch quietly stopped
// returning its own fields, because the owner's shape would look clean when there was nothing to leak. That is the
// same trap another suite's control caught, where a cross-organization negative was passing against an unrouted
// miss.
//
// And the checklist carries a field the grid does not, which proves the two shapes are genuinely different rather
// than one being a subset that happens to pass both lists.

namespace MotsSupplierPortal.Tests.Integration.Documents;

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
public sealed class DocumentsPersonaShapeTests(PostgresApiFixture fixture)
{
    private static readonly string[] OwnerChecklistKeys =
    [
        "documentTypeId", "code", "nameAr", "nameEn", "isRequired", "expiryTracked", "latestDocument",
    ];

    private static readonly string[] ReviewerRowKeys =
    [
        "documentId", "documentTypeCode", "state", "expiresAt", "expiryState", "downloadUrl", "uploadedAt",
    ];

    private static readonly string[] ReviewerOnlyKeys = ["documentId", "downloadUrl", "expiryState", "uploadedAt"];

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
                $"DOC-2026-{Guid.NewGuid().ToString("N")[..6]}",
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

        foreach (var envelopeKey in EnvelopeKeys)
        {
            row.TryGetProperty(envelopeKey, out _).Should().BeFalse();
        }
    }

    [Fact]
    public async Task The_reviewer_row_carries_exactly_the_allow_listed_keys()
    {
        var (_, reviewer, code) = await SeededAsync();

        var body = await reviewer.GetFromJsonAsync<JsonElement>($"/api/v1/suppliers/{code}/documents");

        KeysOf(body).Should().BeEquivalentTo(EnvelopeKeys, "the reviewer's view is the §5.2 envelope");
        var row = body.GetProperty("data").EnumerateArray().First();
        KeysOf(row).Should().BeEquivalentTo(ReviewerRowKeys);
    }

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
