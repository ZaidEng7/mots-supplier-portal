using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Suppliers;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// BRULE-018 / FR-DOC-006 (MSP-68): a document expiring on an ALREADY-APPROVED supplier flags the
/// profile incomplete.
///
/// <para>This behaviour did not exist at all. DocumentCompletenessEvaluator was consulted at exactly
/// two places - the submit gate and the reviewer approval gate - and both are pre-approval. Once a
/// supplier was approved nothing recomputed completeness ever again, so the expiry job moved a
/// document to Expired and the supplier's profile went on looking complete indefinitely.</para>
///
/// <para>The whole chain is exercised here: the real DocumentExpiryJob transitions the document, and
/// the evaluator that the read path uses then reports it. Asserting only the evaluator would prove
/// the flag works while leaving unproven that anything ever sets the state it reads.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class DocumentExpiryFlagsProfileTests(PostgresApiFixture fixture)
{
    private async Task<(Guid SupplierId, Guid DocumentId)> ApprovedSupplierWithDocumentAsync(DateOnly expiry)
    {
        var name = $"Expiry Flag {Guid.NewGuid():N}"[..24];
        await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, name);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var supplier = await db.Suppliers.FirstAsync(s => s.DisplayNameEn == name);
        await db.Suppliers.Where(s => s.Id == supplier.Id).ExecuteUpdateAsync(p => p
            .SetProperty(s => s.OnboardingState, SupplierOnboardingState.Approved)
            .SetProperty(s => s.LifecycleState, SupplierLifecycleState.Active));

        var type = await db.DocumentTypes.FirstAsync(t => t.Code == "tax_certificate");
        var user = await db.Users.FirstAsync(u => u.SupplierId == supplier.Id);

        // Created directly in the Approved state: this class is about what happens to a document
        // that was already accepted, not about the upload path.
        var document = SupplierDocument.CreatePendingScan(
            $"DOC-2026-{Guid.NewGuid().ToString("N")[..6]}",
            supplier.Id, type.Id, 1, "clean/key", "tax.pdf", "application/pdf", 1024, user.Id,
            issueDate: null, expiryDate: expiry, expiryTracked: true,
            today: expiry.AddDays(-1));
        document.MarkScanClean("clean/key");
        document.Approve(user.Id);

        db.SupplierDocuments.Add(document);
        await db.SaveChangesAsync();

        return (supplier.Id, document.Id);
    }

    [Fact]
    public async Task An_expiring_document_flags_an_already_approved_suppliers_profile_incomplete()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var (supplierId, documentId) = await ApprovedSupplierWithDocumentAsync(today.AddDays(1));

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Before: approved supplier, live document, nothing outstanding.
        (await DocumentCompletenessEvaluator.GetProfileIncompleteDocumentTypeCodesAsync(db, supplierId, default))
            .Should().BeEmpty("a supplier with a valid approved document has nothing outstanding");

        // Time passes. Moving the date rather than waiting is the only practical way to test a
        // date-driven job, and it changes only the document's own data - the job's logic is
        // untouched and runs for real below.
        await db.SupplierDocuments.Where(d => d.Id == documentId)
            .ExecuteUpdateAsync(p => p.SetProperty(d => d.ExpiryDate, today.AddDays(-1)));

        var job = scope.ServiceProvider.GetRequiredService<DocumentExpiryJob>();
        await job.RunAsync(default);

        await using var verify = fixture.Services.CreateAsyncScope();
        var vdb = verify.ServiceProvider.GetRequiredService<AppDbContext>();

        (await vdb.SupplierDocuments.AsNoTracking().FirstAsync(d => d.Id == documentId)).State
            .Should().Be(DocumentState.Expired, "the job must actually transition the document");

        (await DocumentCompletenessEvaluator.GetProfileIncompleteDocumentTypeCodesAsync(vdb, supplierId, default))
            .Should().Contain("tax_certificate",
                "BRULE-018: an expired required document flags the profile incomplete, and this " +
                "supplier is already approved - the submit and approval gates never run again");
    }

    [Fact]
    public async Task A_document_merely_expiring_soon_does_not_flag_the_profile()
    {
        // BRULE-018 names Rejected and Expired, not ExpiringSoon. A document valid for another
        // three weeks has not stopped satisfying anything, and flagging it would make "incomplete"
        // the normal condition of every supplier with a renewal approaching - which is how a
        // warning stops being read.
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var (supplierId, documentId) = await ApprovedSupplierWithDocumentAsync(today.AddDays(10));

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var job = scope.ServiceProvider.GetRequiredService<DocumentExpiryJob>();
        await job.RunAsync(default);

        (await db.SupplierDocuments.AsNoTracking().FirstAsync(d => d.Id == documentId)).State
            .Should().Be(DocumentState.ExpiringSoon, "10 days is inside the 30-day window");

        (await DocumentCompletenessEvaluator.GetProfileIncompleteDocumentTypeCodesAsync(db, supplierId, default))
            .Should().NotContain("tax_certificate");
    }

    [Fact]
    public async Task A_rejected_document_also_flags_the_profile()
    {
        // The other half of BRULE-018's wording, and the one with no job involved: a reviewer
        // rejecting a document on an approved supplier flags the profile just as an expiry does.
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var (supplierId, documentId) = await ApprovedSupplierWithDocumentAsync(today.AddDays(365));

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.SupplierDocuments.Where(d => d.Id == documentId)
            .ExecuteUpdateAsync(p => p.SetProperty(d => d.State, DocumentState.Rejected));

        (await DocumentCompletenessEvaluator.GetProfileIncompleteDocumentTypeCodesAsync(db, supplierId, default))
            .Should().Contain("tax_certificate");
    }
}
