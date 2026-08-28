using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Registrations;

/// <summary>
/// FR-REG-007/FR-ADM-011/NFR-PRIV-006: recurring retention/cleanup job for abandoned Draft
/// registrations (never got past email verification) and expired verification/reset tokens.
/// Hard-deletes rather than soft-deletes - an abandoned Draft has no downstream data (no
/// documents, no submitted profile) for anything else to reference, so there's no lifecycle
/// reason to keep a tombstone (NFR-PRIV-006: "soft-delete only where lifecycle demands, otherwise
/// hard delete + audit").
/// </summary>
public sealed class DraftCleanupJob(AppDbContext db, UserManager<AppUser> userManager, IAuditLogger auditLogger)
{
    // [ASSUMPTION] FR-REG-007/NFR-PRIV-006 require a retention policy but don't specify a window;
    // 30 days matches this codebase's other retention-adjacent defaults (refresh-token absolute
    // cap, document-expiry lead time).
    private static readonly TimeSpan AbandonedDraftRetention = TimeSpan.FromDays(30);

    public async Task RunAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow - AbandonedDraftRetention;
        var abandoned = await db.Suppliers
            .Where(s => s.OnboardingState == SupplierOnboardingState.Draft && s.CreatedAt < cutoff)
            .ToListAsync(ct);

        foreach (var supplier in abandoned)
        {
            await auditLogger.LogAsync(
                "Supplier", supplier.Id, "draft_cleanup_deleted", actorLabel: "system", reason: $"abandoned draft older than {AbandonedDraftRetention.Days}d",
                referenceCode: supplier.ReferenceCode, ct: ct);

            var user = await userManager.Users.FirstOrDefaultAsync(u => u.SupplierId == supplier.Id, ct);
            if (user is not null)
            {
                await userManager.DeleteAsync(user);
            }

            db.Suppliers.Remove(supplier);
        }

        if (abandoned.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        await db.SecurityTokens.Where(t => t.ExpiresAt < DateTimeOffset.UtcNow).ExecuteDeleteAsync(ct);
    }
}
