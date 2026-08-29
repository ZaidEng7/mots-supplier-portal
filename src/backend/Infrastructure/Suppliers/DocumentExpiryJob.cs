using Microsoft.Extensions.Configuration;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>
/// FEAT-05.5: recurring job moving Approved -> ExpiringSoon (within a configurable window) and
/// ExpiringSoon -> Expired at expiry. Idempotent - only acts on documents not already in the
/// target state, so re-running (e.g. after a host restart) never double-transitions or
/// double-notifies.
/// </summary>
public sealed class DocumentExpiryJob(
    AppDbContext db,
    IAuditLogger auditLogger,
    IBackgroundJobClient backgroundJobs,
    IConfiguration configuration)
{
    /// <summary>
    /// FR-DOC-006 calls this window configurable; it was a private static readonly const, which is
    /// the opposite. Changing it required a redeploy, and the comment beside it claimed
    /// "configurable" - an artifact asserting something untrue, the pattern this codebase keeps
    /// producing.
    ///
    /// Default stays 30 days so behaviour is unchanged where nothing is configured.
    /// </summary>
    private int ExpiringSoonWindowDays =>
        configuration.GetValue("Documents:ExpiringSoonWindowDays", 30);

    public async Task RunAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);
        var soonThreshold = today.AddDays(ExpiringSoonWindowDays);

        var expiringSoon = await db.SupplierDocuments
            .Where(d => d.IsLatestVersion && d.State == DocumentState.Approved && d.ExpiryDate != null && d.ExpiryDate <= soonThreshold)
            .ToListAsync(ct);

        foreach (var doc in expiringSoon)
        {
            doc.MarkExpiringSoon();
            await auditLogger.LogAsync("SupplierDocument", doc.Id, "document_expiring_soon", ct: ct);
        }

        var expired = await db.SupplierDocuments
            .Where(d => d.IsLatestVersion && (d.State == DocumentState.Approved || d.State == DocumentState.ExpiringSoon) && d.ExpiryDate != null && d.ExpiryDate < today)
            .ToListAsync(ct);

        foreach (var doc in expired)
        {
            doc.MarkExpired();
            await auditLogger.LogAsync("SupplierDocument", doc.Id, "document_expired", ct: ct);
        }

        if (expiringSoon.Count > 0 || expired.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        // Notifications are enqueued after the state-change transaction commits, and only for
        // documents this run actually transitioned - re-running the idempotent job never re-notifies.
        foreach (var doc in expiringSoon)
        {
            var email = await db.Users.Where(u => u.SupplierId == doc.SupplierId).Select(u => u.Email).FirstOrDefaultAsync(ct);
            if (email is not null)
            {
                backgroundJobs.Enqueue<EmailJobs>(job => job.SendDocumentExpiringEmailAsync(email, doc.OriginalFileName, CancellationToken.None));
            }
        }

        foreach (var doc in expired)
        {
            var email = await db.Users.Where(u => u.SupplierId == doc.SupplierId).Select(u => u.Email).FirstOrDefaultAsync(ct);
            if (email is not null)
            {
                backgroundJobs.Enqueue<EmailJobs>(job => job.SendDocumentExpiredEmailAsync(email, doc.OriginalFileName, CancellationToken.None));
            }
        }
    }
}
