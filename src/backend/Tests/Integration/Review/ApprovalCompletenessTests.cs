// BRULE-017 and MSP-91: a required document that is missing or unscanned blocks approval.
//
// Both directions, and the happy-path one is the load-bearing one. A guard that blocked approval whenever any
// document is anything other than Approved would also close the bypass - every "the hole is shut" assertion
// here would still pass - while quietly breaking the ordinary path the 2026-08-26 decision explicitly
// protected. That decision said approval must not require every document to already be Approved: an Uploaded
// document waiting on a reviewer must still let the application through. So the first test approves a supplier
// whose required document is merely Uploaded, and it fails if the predicate is tightened to State == Approved,
// which is the exact error the old implementation was making in the opposite direction.
//
// DO NOT DELETE THAT TEST AS REDUNDANT. It is the only thing separating a correct fix from one that strands
// suppliers mid-onboarding, and it looks like the least interesting test in the file. Revert-to-red, measured,
// both directions: with the predicate too narrow - the old one - two tests fail; with it too broad, blocking
// unless Approved, ONLY that one fails. That asymmetry is the point, because a guard blocking approval unless
// every document is Approved closes the bypass and satisfies every "the hole is shut" assertion here - it is
// indistinguishable from the correct fix by any other test in this class. One boring test is the entire
// difference. A reviewer approving the application before working through each document is the ordinary path,
// not an edge case.
//
// The fixture gives a supplier a document for EVERY required type, with the first one in the requested state
// and the rest Approved, so any blocking result names the document the test actually set up. PendingScan is
// where a fresh upload already sits, so it is reached by doing nothing, and Uploaded needs nothing further
// because MarkScanClean lands there. The first version of the fixture seeded only one document while the
// catalogue has two required types, so the unseeded one blocked and the two "does not block" tests failed -
// that was the fixture being wrong rather than the rule, and it was the new rule doing its job, since under the
// old predicate a required type with no document at all passed silently. Worth leaving recorded: the first
// thing the fix caught was my own incomplete setup.
//
// The remaining tests are the rest of the rule. An approved document does not block. An unscanned one does,
// which is the AV-bypass half: a re-upload during InfoRequested supersedes the approved version and leaves the
// latest in PendingScan, and PendingScan was not in the old predicate, so a supplier could be approved holding
// a required document that had never been scanned. A rejected document still blocks - unchanged behaviour,
// asserted so the rewrite of this predicate cannot quietly drop what it already did correctly. And a required
// document that was never uploaded blocks, which is the case the recorded decision never covered: the old
// "latest is not null &&" meant a MISSING required document passed straight through, reachable when an admin
// activates a new required DocumentType after a supplier has submitted.

namespace MotsSupplierPortal.Tests.Integration.Review;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Suppliers;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class ApprovalCompletenessTests(PostgresApiFixture fixture)
{
    private async Task<Guid> SeedSupplierWithRequiredDocumentAsync(DocumentState state)
    {
        await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"Appr {Guid.NewGuid():N}"[..18]);

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var supplierId = await db.Users.Where(u => u.SupplierId != null)
            .OrderByDescending(u => u.Id).Select(u => u.SupplierId!.Value).FirstAsync();

        var requiredTypeIds = await db.DocumentTypes
            .Where(t => t.IsRequired && t.IsActive).Select(t => t.Id).ToListAsync();

        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);

        foreach (var (typeId, index) in requiredTypeIds.Select((id, i) => (id, i)))
        {
            var document = SupplierDocument.CreatePendingScan(
                $"DOC-2026-{Guid.NewGuid().ToString("N")[..6]}",
                supplierId, typeId, 1, "quarantine/key",
                $"approval-{Guid.NewGuid():N}.pdf", "application/pdf", 1024, Guid.CreateVersion7(),
                issueDate: null, expiryDate: null, expiryTracked: false, today: today);

            var target = index == 0 ? state : DocumentState.Approved;

            if (target != DocumentState.PendingScan)
            {
                document.MarkScanClean("clean/key");

                if (target == DocumentState.Approved) document.Approve(Guid.CreateVersion7());
                else if (target == DocumentState.Rejected) document.Reject(Guid.CreateVersion7(), "Illegible");
            }

            db.SupplierDocuments.Add(document);
        }

        await db.SaveChangesAsync();

        return supplierId;
    }

    private async Task<IReadOnlyList<string>> BlockingCodesAsync(Guid supplierId)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await DocumentCompletenessEvaluator
            .GetBlockingRequiredDocumentTypeCodesAsync(db, supplierId, CancellationToken.None);
    }

    [Fact]
    public async Task An_uploaded_document_awaiting_review_does_NOT_block_approval()
    {
        var supplierId = await SeedSupplierWithRequiredDocumentAsync(DocumentState.Uploaded);

        var blocking = await BlockingCodesAsync(supplierId);

        blocking.Should().BeEmpty(
            "a document waiting on a reviewer is not an incomplete application - the recorded " +
            "decision protects exactly this case, and widening the guard would silently revoke it");
    }

    [Fact]
    public async Task An_approved_document_does_not_block_approval()
    {
        var supplierId = await SeedSupplierWithRequiredDocumentAsync(DocumentState.Approved);

        (await BlockingCodesAsync(supplierId)).Should().BeEmpty();
    }

    [Fact]
    public async Task An_unscanned_document_blocks_approval()
    {
        var supplierId = await SeedSupplierWithRequiredDocumentAsync(DocumentState.PendingScan);

        (await BlockingCodesAsync(supplierId)).Should().NotBeEmpty(
            "approving a document nobody has scanned defeats the point of having an AV pipeline");
    }

    [Fact]
    public async Task A_rejected_document_still_blocks_approval()
    {
        var supplierId = await SeedSupplierWithRequiredDocumentAsync(DocumentState.Rejected);

        (await BlockingCodesAsync(supplierId)).Should().NotBeEmpty();
    }

    [Fact]
    public async Task A_required_document_that_was_never_uploaded_blocks_approval()
    {
        await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"None {Guid.NewGuid():N}"[..18]);

        Guid supplierId;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            supplierId = await db.Users.Where(u => u.SupplierId != null)
                .OrderByDescending(u => u.Id).Select(u => u.SupplierId!.Value).FirstAsync();
        }

        var blocking = await BlockingCodesAsync(supplierId);

        blocking.Should().NotBeEmpty(
            "the submit gate cannot cover this: the required set can change after submission, and " +
            "resubmit is the only other entrance");
    }
}
