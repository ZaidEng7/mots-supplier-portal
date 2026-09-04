using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Suppliers;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// BRULE-017 / MSP-91: a required document that is missing or unscanned blocks approval.
///
/// <para><b>Both directions, and the happy-path one is the load-bearing one.</b> A guard that blocks
/// approval whenever any document is anything other than Approved would also close the bypass -
/// every "the hole is shut" assertion here would still pass - while quietly breaking the ordinary
/// path the 2026-08-26 decision explicitly protected. That decision said approval must not require
/// every document to already be Approved; an Uploaded document waiting on a reviewer must still let
/// the application through.</para>
///
/// <para>So the first test approves a supplier whose required document is merely <c>Uploaded</c>.
/// It fails if the predicate is tightened to <c>State == Approved</c>, which is the exact error the
/// old implementation was making in the opposite direction.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ApprovalCompletenessTests(PostgresApiFixture fixture)
{
    /// <summary>
    /// A supplier holding a document for EVERY required type, with the first one in the requested
    /// state and the rest Approved.
    ///
    /// The first version seeded only one document and the catalogue has two required types, so the
    /// unseeded one blocked and the two "does not block" tests failed. That was the fixture being
    /// wrong rather than the rule - and it is the new rule doing its job, since under the old
    /// predicate a required type with no document at all passed silently. Worth leaving recorded:
    /// the first thing the fix caught was my own incomplete setup.
    /// </summary>
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

            // Only the first document carries the state under test; the rest are Approved, so any
            // blocking result names the document the test actually set up.
            var target = index == 0 ? state : DocumentState.Approved;

            // PendingScan is where a fresh upload already sits, so it is reached by doing nothing.
            if (target != DocumentState.PendingScan)
            {
                document.MarkScanClean("clean/key");

                if (target == DocumentState.Approved) document.Approve(Guid.CreateVersion7());
                else if (target == DocumentState.Rejected) document.Reject(Guid.CreateVersion7(), "Illegible");
                // Uploaded needs nothing further - MarkScanClean lands there.
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

    /// <summary>
    /// DO NOT DELETE THIS AS REDUNDANT. It is the only thing separating a correct fix from one that
    /// strands suppliers mid-onboarding, and it looks like the least interesting test in the file.
    ///
    /// <para>Revert-to-red, measured, both directions:</para>
    /// <list type="table">
    /// <item><term>Predicate too narrow (the old one)</term><description>two tests fail</description></item>
    /// <item><term>Predicate too broad (block unless Approved)</term><description>ONLY this one fails</description></item>
    /// </list>
    ///
    /// <para>That asymmetry is the point. A guard blocking approval unless every document is Approved
    /// closes the bypass and satisfies every "the hole is shut" assertion here - it is
    /// indistinguishable from the correct fix by any other test in this class. One boring test is the
    /// entire difference.</para>
    /// </summary>
    [Fact]
    public async Task An_uploaded_document_awaiting_review_does_NOT_block_approval()
    {
        // THE test that stops this fix from being too broad. The 2026-08-26 product-owner decision
        // is explicit that approval does not require every document to be individually Approved, and
        // a reviewer approving the application before working through each document is the ordinary
        // path - not an edge case. Tighten the predicate to `State == Approved` and this fails while
        // every other test here still passes.
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
        // The AV-bypass half. A re-upload during InfoRequested supersedes the approved version and
        // leaves the latest in PendingScan; PendingScan was not in the old predicate, so a supplier
        // could be approved holding a required document that had never been scanned.
        var supplierId = await SeedSupplierWithRequiredDocumentAsync(DocumentState.PendingScan);

        (await BlockingCodesAsync(supplierId)).Should().NotBeEmpty(
            "approving a document nobody has scanned defeats the point of having an AV pipeline");
    }

    [Fact]
    public async Task A_rejected_document_still_blocks_approval()
    {
        // Unchanged behaviour, asserted so the rewrite of this predicate cannot quietly drop what it
        // already did correctly.
        var supplierId = await SeedSupplierWithRequiredDocumentAsync(DocumentState.Rejected);

        (await BlockingCodesAsync(supplierId)).Should().NotBeEmpty();
    }

    [Fact]
    public async Task A_required_document_that_was_never_uploaded_blocks_approval()
    {
        // The case the recorded decision never covered. `latest is not null &&` meant a MISSING
        // required document passed straight through - reachable when an admin activates a new
        // required DocumentType after a supplier has submitted.
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
