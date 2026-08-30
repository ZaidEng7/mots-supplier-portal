using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Suppliers;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// BRULE-023 (MSP-68): expiry of an award-critical document auto-suspends the supplier.
///
/// <para><b>The negative case is the load-bearing one.</b> A predicate that is too broad suspends
/// suppliers nobody decided to suspend, and it is invisible in a test suite that only checks the
/// rule fires when it should - everything passes, and the damage is to suppliers who were never
/// meant to be in scope. So the second test here expires a document of a NON-award-critical type
/// and requires the supplier to still be Active. That is the assertion that fails if the predicate
/// ever loosens to IsRequired, ExpiryTracked, or anything else convenient.</para>
///
/// <para>This mirrors the MSP-87 lesson from earlier in the week: verifying only that the rule fires
/// cannot distinguish "correctly scoped" from "fires for everyone".</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AwardCriticalSuspensionTests(PostgresApiFixture fixture)
{
    private const string TaxCertificate = "tax_certificate";
    private const string ChamberMembership = "chamber_membership";

    /// <summary>An Active supplier holding one approved, expiry-tracked document of the named type,
    /// already past its expiry date. Built through the real transitions - Submit, PickUpForReview,
    /// Approve - so the supplier is Active for the same reason a real one would be.</summary>
    private async Task<(Guid SupplierId, Guid DocumentId)> SeedActiveSupplierWithExpiredDocumentAsync(
        string documentTypeCode)
    {
        await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"Award {Guid.NewGuid():N}"[..20]);

        Guid supplierId;

        // Onboarding and document upload are committed separately, as they are in production. That
        // is not tidiness: batching the supplier's own UPDATE together with its child inserts is the
        // shape that produces the DbUpdateConcurrencyException documented at AppDbContext line 106,
        // and a fixture that has to work around an EF batching hazard is a fixture asserting
        // something the application never does.
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            supplierId = await db.Users
                .Where(u => u.SupplierId != null)
                .OrderByDescending(u => u.Id)
                .Select(u => u.SupplierId!.Value)
                .FirstAsync();

            // IncludeProfile, not a hand-written Include list. Its own doc comment records why it
            // exists: a partially loaded supplier reports primaryContactPhone as missing. Writing
            // the includes out by hand here reproduced that exact failure, which is a reasonable
            // argument that the shared extension should be the only way anyone loads this aggregate.
            var supplier = await db.Suppliers.IncludeProfile().SingleAsync(s => s.Id == supplierId);

            // Driven through the real onboarding path rather than by writing the state column.
            // BRULE-023 acts on Active suppliers, and one that reached Active by UPDATE could
            // satisfy this test while the real transition had an invariant that would have refused
            // it. Submit itself enumerates what is missing, so this list is the domain's answer
            // rather than a guess.
            var regionCode = await db.Regions.Select(r => r.Code).FirstAsync();
            var categoryCode = await db.Categories.Select(c => c.Code).FirstAsync();

            // Owner-column changes commit on their own, then the child collections. Batching the
            // two together is the shape that produces the DbUpdateConcurrencyException documented at
            // AppDbContext line 106: EF emits a second UPDATE against the supplier row, checked
            // against an xmin the first UPDATE has already bumped.
            supplier.UpdateCoreProfile("Award-critical suspension fixture", null, null, "SYP");
            supplier.AcceptTerms("v1");

            // Registration creates the representative but marks none primary, and the submit gate
            // wants a primary WITH a phone.
            var representative = supplier.Representatives[0];
            supplier.SetPrimaryRepresentative(representative.Id);
            supplier.UpdateRepresentative(representative.Id, representative.FullName,
                representative.Email, "+963900000002", null);

            await db.SaveChangesAsync();

            // Tracked explicitly, exactly as ManageAddressHandler does and for the reason recorded
            // there: these Ids are client-assigned by the domain factory, so EF's graph-tracking
            // heuristic infers Modified from the non-default key and emits a no-op UPDATE instead of
            // an INSERT. Without this the save fails with DbUpdateConcurrencyException naming
            // Address:Modified - which is what it did.
            db.Addresses.Add(supplier.AddAddress(AddressKind.HeadOffice, "1 Test Street", null,
                "Damascus", regionCode, "SY", null, null, null));
            db.Contacts.Add(supplier.AddContact(
                "Award Fixture Contact", "award@example.com", "+963900000001", "primary"));

            var (link, _) = supplier.LinkCategory(categoryCode, isComplianceCritical: false);
            if (link is not null) db.CategoryLinks.Add(link);

            await db.SaveChangesAsync();
        }

        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Submit reads every collection to decide what is missing, so this must be the full
            // aggregate too.
            var supplier = await db.Suppliers.IncludeProfile().SingleAsync(s => s.Id == supplierId);

            supplier.Submit([]);
            supplier.PickUpForReview();
            supplier.Approve([]);

            await db.SaveChangesAsync();
        }

        Guid documentId;

        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var typeId = await db.DocumentTypes.Where(t => t.Code == documentTypeCode)
                .Select(t => t.Id).SingleAsync();

            var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);

            var document = SupplierDocument.CreatePendingScan(
                supplierId, typeId, 1, "quarantine/key", $"award-{Guid.NewGuid():N}.pdf",
                "application/pdf", 2048, Guid.CreateVersion7(),
                issueDate: null, expiryDate: today.AddDays(1), expiryTracked: true, today: today);

            document.MarkScanClean("clean/key");
            document.Approve(Guid.CreateVersion7());

            db.SupplierDocuments.Add(document);
            await db.SaveChangesAsync();

            // Backdate past expiry directly. The aggregate refuses a past expiry at upload
            // (BRULE-020, correctly), so the only way to reach "an approved document that has since
            // expired" - which is the ordinary passage of time in production - is to write the date
            // the clock would have produced.
            await db.Database.ExecuteSqlAsync(
                $"UPDATE supplier.supplier_document SET \"ExpiryDate\" = {today.AddDays(-1)} WHERE \"Id\" = {document.Id}");

            documentId = document.Id;
        }

        return (supplierId, documentId);
    }

    private async Task SetAwardCriticalAsync(string code, bool value)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.ExecuteSqlAsync(
            $"UPDATE reference.document_type SET \"IsAwardCritical\" = {value} WHERE \"Code\" = {code}");
    }

    private async Task RunExpiryJobAsync()
    {
        using var scope = fixture.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<Infrastructure.Suppliers.DocumentExpiryJob>();
        await job.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task An_expired_award_critical_document_suspends_the_supplier_and_says_why()
    {
        await SetAwardCriticalAsync(TaxCertificate, true);
        try
        {
            var (supplierId, _) = await SeedActiveSupplierWithExpiredDocumentAsync(TaxCertificate);

            await RunExpiryJobAsync();

            using var scope = fixture.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var state = await db.Suppliers.Where(s => s.Id == supplierId)
                .Select(s => s.LifecycleState).SingleAsync();

            state.Should().Be(SupplierLifecycleState.Suspended);

            var audit = await db.AuditLogs
                .Where(a => a.AggregateId == supplierId && a.Action == "supplier_auto_suspended")
                .SingleAsync();

            audit.Reason.Should().Contain(TaxCertificate,
                "the supplier's support conversation starts from this row - 'suspended' alone " +
                "leaves whoever answers the phone with nothing");
            audit.Reason.Should().Contain("BRULE-023");
            audit.FromState.Should().Be(nameof(SupplierLifecycleState.Active));
            audit.ToState.Should().Be(nameof(SupplierLifecycleState.Suspended));
        }
        finally
        {
            await SetAwardCriticalAsync(TaxCertificate, false);
        }
    }

    [Fact]
    public async Task An_expired_document_that_is_not_award_critical_suspends_nobody()
    {
        // The case a too-broad predicate makes invisible. chamber_membership is expiry-tracked and
        // will expire in exactly the same way; the only thing that must keep it out of scope is the
        // IsAwardCritical flag itself.
        var (supplierId, _) = await SeedActiveSupplierWithExpiredDocumentAsync(ChamberMembership);

        await RunExpiryJobAsync();

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var state = await db.Suppliers.Where(s => s.Id == supplierId)
            .Select(s => s.LifecycleState).SingleAsync();

        state.Should().Be(SupplierLifecycleState.Active,
            "expiry of a document nobody marked award-critical is a compliance flag, not a " +
            "participation block");

        var auditRows = await db.AuditLogs
            .Where(a => a.AggregateId == supplierId && a.Action == "supplier_auto_suspended")
            .CountAsync();

        auditRows.Should().Be(0);
    }

    [Fact]
    public async Task Nothing_is_award_critical_by_default()
    {
        // BRULE-023 ships dormant: which types are award-critical is [REQUIRES BUSINESS
        // CONFIRMATION], and flagging one on a guess suspends real suppliers, which reactivation
        // does not undo. This asserts the shipped state so nobody quietly picks a default later
        // without the Ministry's answer - see docs/product/BLOCKED-DECISIONS.md.
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var flagged = await db.DocumentTypes.Where(t => t.IsAwardCritical)
            .Select(t => t.Code).ToListAsync();

        flagged.Should().BeEmpty(
            "the mechanism is complete and the decision is not ours; the Ministry's answer is a " +
            "data change, not a deployment");
    }
}
