using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Identity;

/// <summary>Shared by the three staff mutations: load the account, refuse a supplier's user, and read
/// it back in the shape the list uses.</summary>
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

    /// <summary>
    /// True when deactivating or demoting this account would leave no active `system_admin`.
    ///
    /// <para>The same refusal `UpdateRolePermissions` makes about the last `admin.roles.manage`, for the
    /// same reason: the recovery path afterwards is a hand-written database update, and a product that
    /// can lock every administrator out of itself through its own UI has a defect.</para>
    /// </summary>
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
