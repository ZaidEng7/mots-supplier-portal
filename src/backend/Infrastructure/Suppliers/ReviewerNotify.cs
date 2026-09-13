using System.Text.Json;
using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Infrastructure.Configuration;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

internal static class ReviewerNotify
{
    /// <summary>The supplier's primary user, as an id rather than an address (MSP-89). The address
    /// is resolved inside the job so it never reaches the Hangfire store.</summary>
    public static async Task<Guid?> GetPrimaryUserIdAsync(AppDbContext db, Guid supplierId, CancellationToken ct) =>
        await db.Users.Where(u => u.SupplierId == supplierId)
            .Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);

    /// <summary>BUSINESS-PROCESSES.md: InfoRequested -> Resubmitted notifies "reviewer" - there is
    /// no per-application reviewer assignment (pickup doesn't record who), so this notifies the
    /// whole onboarding_reviewer pool, matching how Submit already "queue[s] to onboarding review
    /// pool" rather than a named individual.</summary>
    public static async Task<IReadOnlyList<Guid>> GetReviewerPoolUserIdsAsync(AppDbContext db, CancellationToken ct) =>
        await (from ur in db.UserRoles
               join r in db.Roles on ur.RoleId equals r.Id
               join u in db.Users on ur.UserId equals u.Id
               where r.Name == Roles.OnboardingReviewer
               select u.Id)
            .Distinct()
            .ToListAsync(ct);
}
