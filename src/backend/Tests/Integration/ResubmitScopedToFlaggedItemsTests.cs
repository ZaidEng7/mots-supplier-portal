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

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// Task #32 / SUP-2026-000044: a document a reviewer rejects independently of any info request
/// (the separate per-document approve/reject action, FEAT-05.4) must not permanently block
/// resolving what a later request-info actually flagged.
///
/// Before this fix, <c>Resubmit</c> demanded every required document satisfy the submit gate
/// unconditionally - the rejected-but-unflagged document blocked it forever, and re-uploading
/// that document was itself refused because it was not in the annotation's flagged set
/// (UploadDocumentHandler). A second request-info to flag it was also refused, because
/// RequestInfo only runs from UnderReview and the application was stuck in InfoRequested - the
/// only door out required walking through a door locked from the far side. This is the exact
/// sequence that happened to SUP-2026-000044 during manual testing.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ResubmitScopedToFlaggedItemsTests(PostgresApiFixture fixture)
{
    /// <summary>Drives the domain directly through Submit -> UnderReview -> (one document
    /// rejected) -> InfoRequested (flagging a DIFFERENT field), matching how FlaggedFieldEnforcementTests
    /// sets up InfoRequested state - the reviewer endpoints need a separate staff identity, which
    /// is not what either test in this class is verifying.</summary>
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
            // Uploaded already satisfies SatisfiesSubmitRequirement, so Submit() below succeeds
            // without approving anything first. The first document is left Uploaded deliberately -
            // Reject() only accepts 'Uploaded' or 'UnderReview' as a starting state, and rejecting it
            // AFTER submission (below) mirrors the real timeline: a reviewer cannot reject a document
            // that was never uploaded.
            if (index != 0) document.Approve(Guid.CreateVersion7());
            db.SupplierDocuments.Add(document);
            if (index == 0) rejectedDocumentId = document.Id;
        }
        await db.SaveChangesAsync();

        supplier.Submit([]);
        supplier.PickUpForReview();
        await db.SaveChangesAsync();

        // The independent per-document reject (FEAT-05.4) - nothing to do with the info request
        // that follows. Re-fetch so the reject is applied to a tracked entity from this SaveChanges.
        var rejectedDocument = await db.SupplierDocuments.SingleAsync(d => d.Id == rejectedDocumentId);
        rejectedDocument.Reject(Guid.CreateVersion7(), "Illegible - independent of the info request below");

        // The info request flags primaryContactPhone - the same field flagged on the real
        // SUP-2026-000044 - deliberately NOT the rejected document.
        supplier.RequestInfo();

        // Registration itself supplies a primary representative with a phone (RegisterSupplierHandler),
        // which is why Submit() above could succeed. Blanking it here - only now editable, since
        // InfoRequested is one of the states EnsureEditable allows - is what makes primaryContactPhone
        // the thing genuinely missing when the reviewer flags it, matching the real SUP-2026-000044
        // report ("Please provide a valid phone number with country code").
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

        // Fix exactly what was flagged - nothing else changes.
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
        // The other half: narrowing the check to flagged items must not become "nothing blocks
        // resubmit". The one thing actually asked for still has to be fixed.
        var (client, _, _) = await CreateSupplierWithRejectedDocumentAndUnrelatedFlagAsync();

        var resubmitResponse = await client.PostAsync("/api/v1/suppliers/me/resubmit-application", null);

        resubmitResponse.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "primaryContactPhone was flagged and was never corrected - resubmit must still refuse");
    }
}
