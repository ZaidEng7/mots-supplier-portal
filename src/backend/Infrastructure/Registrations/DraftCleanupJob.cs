// The retention job: it removes abandoned draft registrations and expired tokens.
//
// An abandoned draft is one that never got past email verification.
//
// It hard-deletes rather than soft-deletes. An abandoned draft has no downstream data, no documents and no
// submitted profile, for anything else to reference, so there is no lifecycle reason to keep a tombstone. The
// written privacy rule says soft-delete only where the lifecycle demands it and otherwise delete and audit.
//
// The window is an assumption. The requirements ask for a retention policy and do not specify a period, so it
// matches this codebase's other retention-adjacent defaults.

namespace MotsSupplierPortal.Infrastructure.Registrations;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class DraftCleanupJob(AppDbContext db, UserManager<AppUser> userManager, IAuditLogger auditLogger)
{
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
