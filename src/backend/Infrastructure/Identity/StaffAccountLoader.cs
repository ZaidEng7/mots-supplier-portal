// The load, the refusal and the read-back that the three staff mutations share.
//
// Only accounts with no company attached are staff, so a supplier's user cannot be administered from the staff
// surface.
//
// The lockout check is the same refusal the role-permission update makes about the last role holding the
// management permission, and for the same reason: the recovery path afterwards is a hand-written database
// update, and a product that can lock every administrator out of itself through its own interface has a
// defect.

namespace MotsSupplierPortal.Infrastructure.Identity;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

internal static class StaffAccountLoader
{
    public static Task<AppUser?> LoadAsync(UserManager<AppUser> userManager, Guid userId, CancellationToken ct) =>
        userManager.Users.FirstOrDefaultAsync(u => u.Id == userId && u.SupplierId == null, ct);

    public static async Task<StaffAccountDto> ToDtoAsync(AppDbContext db, UserManager<AppUser> userManager, AppUser user, CancellationToken ct)
    {
        var role = (await userManager.GetRolesAsync(user)).FirstOrDefault();
        var sessions = await db.RefreshTokens.CountAsync(t => t.UserId == user.Id && t.RevokedAt == null, ct);
        return new StaffAccountDto(user.Id, user.Email!, user.FullName, role, user.IsActive,
            user.TwoFactorEnabled, user.LockoutEnd, sessions);
    }

    public static async Task<bool> WouldLeaveNoAdministratorAsync(
        AppDbContext db, UserManager<AppUser> userManager, AppUser user, CancellationToken ct)
    {
        var roles = await userManager.GetRolesAsync(user);
        if (!roles.Contains(Roles.SystemAdmin)) return false;

        var adminRoleId = await db.Set<IdentityRole<Guid>>()
            .Where(r => r.Name == Roles.SystemAdmin).Select(r => r.Id).FirstOrDefaultAsync(ct);

        var otherActiveAdmins = await db.Set<IdentityUserRole<Guid>>()
            .Where(ur => ur.RoleId == adminRoleId && ur.UserId != user.Id)
            .Join(db.Users.Where(u => u.IsActive), ur => ur.UserId, u => u.Id, (ur, u) => u.Id)
            .CountAsync(ct);

        return otherActiveAdmins == 0;
    }
}
