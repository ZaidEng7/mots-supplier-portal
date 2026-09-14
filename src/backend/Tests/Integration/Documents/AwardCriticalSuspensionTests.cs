// Expiry of an award-critical document suspends the supplier.
//
//
// THE NEGATIVE CASE IS THE LOAD-BEARING ONE
//
// A test that is too broad suspends suppliers nobody decided to suspend, and it is invisible in a suite that only
// checks the rule fires when it should: everything passes, and the damage is to suppliers who were never meant to
// be in scope.
//
// So one test expires a document of a NON-award-critical type and requires the supplier to still be active. That
// is the assertion that fails if the test ever loosens to "required", "tracked for expiry", or anything else
// convenient.
//
// This mirrors a lesson from elsewhere in the same week: verifying only that a rule fires cannot distinguish
// correctly scoped from fires for everyone.
//
//
// THE SUPPLIER IS BUILT THROUGH THE REAL TRANSITIONS
//
// Not by writing the state column. The rule acts on active suppliers, and one that reached active by a direct
// update could satisfy this test while the real transition had an invariant that would have refused it.
//
// Submission itself enumerates what is missing, so the profile this seeds is the domain's answer rather than a
// guess. It loads the full aggregate through the shared include rather than a hand-written list, because that
// extension's own header records why it exists: a partially loaded supplier reports a field as missing. Writing
// the includes out by hand here reproduced that exact failure, which is a reasonable argument that the shared
// extension should be the only way anyone loads this record.
//
// Registration creates the representative but marks none primary, and the submit gate wants a primary with a
// phone number.
//
// The new address is tracked explicitly, exactly as the production handler does and for the reason recorded
// there: the identifier is assigned by the domain factory, so the change tracker infers an existing row from the
// set key and emits a pointless update instead of an insert. Without it the save failed naming that entity.
//
//
// ONBOARDING AND THE UPLOAD COMMIT SEPARATELY, AS THEY DO IN PRODUCTION
//
// That is not tidiness. Batching the supplier's own update together with its child inserts is the shape that
// produces the concurrency failure the database context documents, where a second update is emitted against a row
// whose version the first has already advanced.
//
// A fixture that has to work around a batching hazard is a fixture asserting something the application never
// does.
//
//
// THE EXPIRY DATE IS BACKDATED DIRECTLY
//
// The record refuses a past expiry at upload, correctly, so the only way to reach an approved document that has
// SINCE expired, which is the ordinary passage of time in production, is to write the date the clock would have
// produced.
//
//
// WHAT THE FLAG IS NOT SET ON
//
// The award-critical type carries the flag as seeded data now, so this no longer sets it and must not clear it
// afterwards: the toggle that used to wrap the test would have left the shipped value off for whatever ran next.
//
// What is worth pinning is the narrow half: the type the negative test depends on is still off, and that is what
// keeps that test capable of failing.

namespace MotsSupplierPortal.Tests.Integration.Documents;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Suppliers;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class AwardCriticalSuspensionTests(PostgresApiFixture fixture)
{
    private const string TaxCertificate = "tax_certificate";
    private const string ChamberMembership = "chamber_membership";

    private async Task<(Guid SupplierId, Guid DocumentId)> SeedActiveSupplierWithExpiredDocumentAsync(
        string documentTypeCode)
    {
        await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"Award {Guid.NewGuid():N}"[..20]);

        Guid supplierId;

        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            supplierId = await db.Users
                .Where(u => u.SupplierId != null)
                .OrderByDescending(u => u.Id)
                .Select(u => u.SupplierId!.Value)
                .FirstAsync();

            var supplier = await db.Suppliers.IncludeProfile().SingleAsync(s => s.Id == supplierId);

            var regionCode = await db.Regions.Select(r => r.Code).FirstAsync();
            var categoryCode = await db.Categories.Select(c => c.Code).FirstAsync();

            supplier.UpdateCoreProfile("Award-critical suspension fixture", null, null, "SYP");
            supplier.AcceptTerms("v1");

            var representative = supplier.Representatives[0];
            supplier.SetPrimaryRepresentative(representative.Id);
            supplier.UpdateRepresentative(representative.Id, representative.FullName,
                representative.Email, "+963900000002", null);

            await db.SaveChangesAsync();

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
                $"DOC-2026-{Guid.NewGuid().ToString("N")[..6]}",
                supplierId, typeId, 1, "quarantine/key", $"award-{Guid.NewGuid():N}.pdf",
                "application/pdf", 2048, Guid.CreateVersion7(),
                issueDate: null, expiryDate: today.AddDays(1), expiryTracked: true, today: today);

            document.MarkScanClean("clean/key");
            document.Approve(Guid.CreateVersion7());

            db.SupplierDocuments.Add(document);
            await db.SaveChangesAsync();

            await db.Database.ExecuteSqlAsync(
                $"UPDATE supplier.supplier_document SET \"ExpiryDate\" = {today.AddDays(-1)} WHERE \"Id\" = {document.Id}");

            documentId = document.Id;
        }

        return (supplierId, documentId);
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

    [Fact]
    public async Task An_expired_document_that_is_not_award_critical_suspends_nobody()
    {
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
    public async Task The_type_the_negative_case_relies_on_is_not_award_critical()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var isCritical = await db.DocumentTypes.Where(t => t.Code == ChamberMembership)
            .Select(t => t.IsAwardCritical).SingleAsync();

        isCritical.Should().BeFalse(
            "if chamber membership were ever marked, An_expired_document_that_is_not_award_critical_" +
            "suspends_nobody would be asserting nothing at all, and the too-broad predicate it exists " +
            "to catch would pass unnoticed");
    }
}
