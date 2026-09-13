// Who to tell when an application moves, resolved as identifiers rather than addresses.
//
// The supplier's own primary user comes back as an identifier. The address is resolved inside the job, so it
// never reaches the background-job store.
//
// "Notify the reviewer" has no named individual to notify: picking an application up does not record who
// did it, so there is no per-application assignment. It notifies the whole reviewer pool, matching the way
// submission already queues to that pool rather than to a person.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

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

internal static class ReviewerNotify
{
    public static async Task<Guid?> GetPrimaryUserIdAsync(AppDbContext db, Guid supplierId, CancellationToken ct) =>
        await db.Users.Where(u => u.SupplierId == supplierId)
            .Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);

    public static async Task<IReadOnlyList<Guid>> GetReviewerPoolUserIdsAsync(AppDbContext db, CancellationToken ct) =>
        await (from ur in db.UserRoles
               join r in db.Roles on ur.RoleId equals r.Id
               join u in db.Users on ur.UserId equals u.Id
               where r.Name == Roles.OnboardingReviewer
               select u.Id)
            .Distinct()
            .ToListAsync(ct);
}
