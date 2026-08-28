using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// MSP-64: the audit logger must not commit the caller's uncommitted work.
///
/// The original defect was not the wrong status code on a concurrency clash - that was a symptom.
/// It was that <c>AuditLogger</c> called <c>SaveChangesAsync</c> on the caller's context, so writing
/// an audit row flushed whatever domain changes happened to be pending. That destroys the caller's
/// atomicity boundary: an operation that fails after auditing but before its own save leaves its
/// domain change persisted anyway, and no rollback can recover it because it was already committed.
///
/// This is deliberately NOT tested through the MSP-65 concurrency case. That case cannot
/// distinguish the two states any more, because SupplierConcurrency.TryPersistAsync wraps the audit
/// call and the save together and catches DbUpdateConcurrencyException from either - so it passes
/// whether or not the logger saves. Testing the property directly avoids routing through it.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AuditTransactionOwnershipTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task Audit_write_does_not_commit_the_callers_pending_domain_changes()
    {
        // Create one rather than assuming the shared database already holds a supplier: run in
        // isolation this class would otherwise fail on an empty table for reasons unrelated to what
        // it tests.
        await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Ownership Probe Co");

        Guid supplierId;
        string? originalDescription;

        await using (var arrange = fixture.Services.CreateAsyncScope())
        {
            var db = arrange.ServiceProvider.GetRequiredService<AppDbContext>();
            var supplier = await db.Suppliers.FirstAsync();
            supplierId = supplier.Id;
            originalDescription = supplier.Description;
        }

        // One scope standing in for one unit of work: mutate, audit, then fail before saving.
        await using (var work = fixture.Services.CreateAsyncScope())
        {
            var db = work.ServiceProvider.GetRequiredService<AppDbContext>();
            var auditLogger = work.ServiceProvider.GetRequiredService<IAuditLogger>();

            var supplier = await db.Suppliers.FirstAsync(s => s.Id == supplierId);
            supplier.UpdateCoreProfile("MUTATED BUT NEVER COMMITTED", supplier.Website, supplier.SupplierGroup, supplier.CurrencyCode);

            await auditLogger.LogAsync("Supplier", supplierId, "ownership_probe");

            // No SaveChangesAsync. This is the handler failing after the audit call - a validation
            // error, a downstream timeout, an exception. The domain change must not survive it.
        }

        await using var verify = fixture.Services.CreateAsyncScope();
        var vdb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var after = await vdb.Suppliers.AsNoTracking().FirstAsync(s => s.Id == supplierId);

        after.Description.Should().Be(originalDescription,
            "writing an audit row must not flush the caller's pending changes; if it does, an " +
            "operation that fails before its own save leaves a partial write behind that no " +
            "rollback can undo");
    }

    [Fact]
    public async Task Audit_row_itself_is_not_written_when_the_caller_never_saves()
    {
        // The other half of the same boundary, and the reason the three added SaveChangesAsync
        // calls exist. Audit rows are now the caller's to commit like any other change: a caller
        // that never saves writes nothing at all, rather than writing the audit row and nothing
        // else - which would be an audit trail claiming an action that did not take effect.
        await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Ownership Probe Two Co");
        var probeAction = $"ownership_probe_{Guid.NewGuid():N}";

        await using (var work = fixture.Services.CreateAsyncScope())
        {
            var db = work.ServiceProvider.GetRequiredService<AppDbContext>();
            var auditLogger = work.ServiceProvider.GetRequiredService<IAuditLogger>();
            var supplier = await db.Suppliers.FirstAsync();

            await auditLogger.LogAsync("Supplier", supplier.Id, probeAction);
        }

        await using var verify = fixture.Services.CreateAsyncScope();
        var vdb = verify.ServiceProvider.GetRequiredService<AppDbContext>();

        (await vdb.AuditLogs.AnyAsync(a => a.Action == probeAction)).Should().BeFalse(
            "an audit row is part of the caller's unit of work, not a side effect that commits itself");
    }
}
