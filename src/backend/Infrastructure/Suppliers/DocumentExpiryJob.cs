using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>
/// FEAT-05.5: recurring job moving Approved -> ExpiringSoon (within a configurable window) and
/// ExpiringSoon -> Expired at expiry. Idempotent - only acts on documents not already in the
/// target state, so re-running (e.g. after a host restart) never double-transitions or
/// double-notifies.
/// </summary>
public sealed class DocumentExpiryJob(AppDbContext db, IAuditLogger auditLogger)
{
    private static readonly TimeSpan ExpiringSoonWindow = TimeSpan.FromDays(30); // [ASSUMPTION] matches FEAT-05.5's "configurable window"

    public async Task RunAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);
        var soonThreshold = today.AddDays(ExpiringSoonWindow.Days);

        var expiringSoon = await db.SupplierDocuments
            .Where(d => d.IsLatestVersion && d.State == DocumentState.Approved && d.ExpiryDate != null && d.ExpiryDate <= soonThreshold)
            .ToListAsync(ct);

        foreach (var doc in expiringSoon)
        {
            doc.MarkExpiringSoon();
            await auditLogger.LogAsync("SupplierDocument", doc.Id, "document_expiring_soon", Guid.NewGuid(), ct: ct);
        }

        var expired = await db.SupplierDocuments
            .Where(d => d.IsLatestVersion && (d.State == DocumentState.Approved || d.State == DocumentState.ExpiringSoon) && d.ExpiryDate != null && d.ExpiryDate < today)
            .ToListAsync(ct);

        foreach (var doc in expired)
        {
            doc.MarkExpired();
            await auditLogger.LogAsync("SupplierDocument", doc.Id, "document_expired", Guid.NewGuid(), ct: ct);
        }

        if (expiringSoon.Count > 0 || expired.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }
}
