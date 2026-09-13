// A document rejected outside an information request must not permanently block resolving what a later request
// actually flagged.
//
//
// THE DEADLOCK THIS FIXES, WHICH HAPPENED TO A REAL SUPPLIER
//
// Resubmission demanded that every required document satisfy the submit gate unconditionally, so the
// rejected-but-unflagged document blocked it forever.
//
// Re-uploading that document was itself refused, because it was not in the request's flagged set. A second request
// to flag it was also refused, because requesting information only runs from the under-review state and the
// application was stuck in the information-requested one.
//
// The only door out required walking through a door locked from the far side.
//
//
// THE TIMELINE IS THE REAL ONE
//
// The first document is left merely uploaded deliberately, which already satisfies the submit requirement, so
// submission succeeds without approving anything.
//
// The rejection then happens AFTER submission, which mirrors the real sequence: a reviewer cannot reject a document
// that was never uploaded, and the rejection only accepts a document in one of those two states.
//
// The information request flags a DIFFERENT thing, the same field flagged on the real case, and the phone number is
// blanked at that point, only now editable, which is what makes it the thing genuinely missing when the reviewer
// flags it.
//
// Registration itself supplies a primary representative with a phone number, which is why submission could succeed
// earlier.
//
// It drives the domain directly rather than the reviewer endpoints, matching another suite's setup, because those
// endpoints need a separate staff identity and that is not what either test here is verifying.
//
//
// THE OTHER HALF
//
// Narrowing the check to flagged items must not become "nothing blocks resubmission". The one thing actually asked
// for still has to be fixed.

namespace MotsSupplierPortal.Tests.Integration.Suppliers;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Suppliers;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class ResubmitScopedToFlaggedItemsTests(PostgresApiFixture fixture)
{
    private async Task<(HttpClient client, string referenceCode, Guid rejectedDocumentId)> CreateSupplierWithRejectedDocumentAndUnrelatedFlagAsync()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Deadlock Repro Co");
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/suppliers/me");
        var referenceCode = me.GetProperty("supplierCode").GetString()!;

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplier = db.Suppliers.IncludeProfile().Single(s => s.ReferenceCode == referenceCode);

        supplier.UpdateCoreProfile("seed", null, null, "SYP");
        var seedAddress = supplier.AddAddress(AddressKind.HeadOffice, "1 Seed Street", null, "Damascus", "DIM", "Syria", null, null, null);
        db.Addresses.Add(seedAddress);
        var (seedLink, _) = supplier.LinkCategory("catering", isComplianceCritical: false);
        if (seedLink is not null) db.CategoryLinks.Add(seedLink);
        supplier.AcceptTerms(Supplier.CurrentTermsVersion);

        var requiredTypeIds = await db.DocumentTypes.Where(t => t.IsRequired && t.IsActive).Select(t => t.Id).ToListAsync();
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);
        Guid rejectedDocumentId = default;

        foreach (var (typeId, index) in requiredTypeIds.Select((id, i) => (id, i)))
        {
            var document = SupplierDocument.CreatePendingScan(
                $"DOC-2026-{Guid.NewGuid().ToString("N")[..6]}",
                supplier.Id, typeId, 1, "quarantine/key",
                $"seed-{Guid.NewGuid():N}.pdf", "application/pdf", 1024, Guid.CreateVersion7(),
                issueDate: null, expiryDate: null, expiryTracked: false, today: today);
            document.MarkScanClean("clean/key");
            if (index != 0) document.Approve(Guid.CreateVersion7());
            db.SupplierDocuments.Add(document);
            if (index == 0) rejectedDocumentId = document.Id;
        }
        await db.SaveChangesAsync();

        supplier.Submit([]);
        supplier.PickUpForReview();
        await db.SaveChangesAsync();

        var rejectedDocument = await db.SupplierDocuments.SingleAsync(d => d.Id == rejectedDocumentId);
        rejectedDocument.Reject(Guid.CreateVersion7(), "Illegible - independent of the info request below");

        supplier.RequestInfo();

        var primaryRep = supplier.Representatives.Single(r => r.IsPrimary);
        supplier.UpdateRepresentative(primaryRep.Id, primaryRep.FullName, primaryRep.Email, phone: null, primaryRep.Position);

        db.SupplierReviewAnnotations.Add(new SupplierReviewAnnotation
        {
            Id = Guid.CreateVersion7(),
            SupplierId = supplier.Id,
            RequestedAt = DateTimeOffset.UtcNow,
            Reason = "Please provide a valid phone number with country code.",
            FlaggedProfileFields = [ProfileFieldCodes.PrimaryContactPhone],
            FlaggedDocumentTypeIds = [],
        });
        await db.SaveChangesAsync();

        return (client, referenceCode, rejectedDocumentId);
    }

    [Fact]
    public async Task Resubmit_succeeds_once_the_flagged_field_is_fixed_even_with_an_unrelated_document_still_rejected()
    {
        var (client, referenceCode, rejectedDocumentId) = await CreateSupplierWithRejectedDocumentAndUnrelatedFlagAsync();

        var patchResponse = await client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/suppliers/{await client.OwnSupplierCodeAsync()}")
        {
            Content = new StringContent("""{"primaryContactPhone":"+963988112233"}""", Encoding.UTF8, "application/json"),
        });
        patchResponse.StatusCode.Should().Be(HttpStatusCode.OK, "the flagged field is exactly what InfoRequested allows editing");

        var resubmitResponse = await client.PostAsync("/api/v1/suppliers/me/resubmit-application", null);

        resubmitResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "the unrelated rejected document was never flagged in this info request and must not block resolving what was actually asked for");
        var body = await resubmitResponse.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("onboardingState").GetString().Should().Be("UnderReview",
            "ResubmitApplicationHandler chains PickUpForReview immediately after a successful Resubmit");

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var annotation = await db.SupplierReviewAnnotations
            .Where(a => a.SupplierId == db.Suppliers.Where(s => s.ReferenceCode == referenceCode).Select(s => s.Id).Single())
            .OrderByDescending(a => a.RequestedAt)
            .FirstAsync();
        annotation.ResolvedAt.Should().NotBeNull("a successful resubmit must resolve the annotation that gated it");

        var rejectedDocument = await db.SupplierDocuments.SingleAsync(d => d.Id == rejectedDocumentId);
        rejectedDocument.State.Should().Be(DocumentState.Rejected,
            "the fix must not silently touch a document that was never part of this info request");
    }

    [Fact]
    public async Task Resubmit_still_refuses_when_the_flagged_item_itself_remains_unaddressed()
    {
        var (client, _, _) = await CreateSupplierWithRejectedDocumentAndUnrelatedFlagAsync();

        var resubmitResponse = await client.PostAsync("/api/v1/suppliers/me/resubmit-application", null);

        resubmitResponse.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "primaryContactPhone was flagged and was never corrected - resubmit must still refuse");
    }
}
