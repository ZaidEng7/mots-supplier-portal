using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Identity;

/// <summary>
/// T-077/SCR-701. The staff list, which did not exist.
///
/// <para>Staff are the accounts with NO SupplierId - the same predicate InviteStaffHandler's own doc
/// comment describes from the other side. A supplier's users are administered by that supplier
/// (SCR-160), not from here, and mixing the two would put a supplier's team in the platform
/// administrator's list.</para>
/// </summary>
public sealed class ListStaffHandler(AppDbContext db) : IListStaffHandler
{
    public async Task<ListEnvelope<StaffAccountDto>> HandleAsync(string? cursor, int? limit, bool withCount, CancellationToken ct)
    {
        var pageSize = ListEnvelope<StaffAccountDto>.ClampPageSize(limit);
        var query = db.Users.Where(u => u.SupplierId == null);

        // §6.1: counted over the filtered set BEFORE the cursor narrows it, and only when asked.
        int? totalCount = withCount ? await query.CountAsync(ct) : null;

        // The cursor type is the supplier-user one, reused rather than copied: the ordering is the same
        // (email, id) and a second identical struct would be a second thing to keep in step.
        if (SupplierUserCursor.TryDecode(cursor, out var from))
        {
            query = query.Where(u =>
                u.Email!.CompareTo(from.Email) > 0
                || (u.Email == from.Email && u.Id.CompareTo(from.Id) > 0));
        }

        var rows = await query
            .OrderBy(u => u.Email).ThenBy(u => u.Id)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.FullName,
                u.IsActive,
                u.TwoFactorEnabled,
                u.LockoutEnd,
                ActiveSessions = db.RefreshTokens.Count(t => t.UserId == u.Id && t.RevokedAt == null),
            })
            .Take(pageSize + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > pageSize;
        var items = hasMore ? rows[..pageSize] : rows;

        // Roles come from Identity's join table, one query for the page rather than one per row.
        var pageIds = items.Select(r => r.Id).ToList();
        var roleByUser = await db.Set<IdentityUserRole<Guid>>()
            .Where(ur => pageIds.Contains(ur.UserId))
            .Join(db.Set<IdentityRole<Guid>>(), ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .ToListAsync(ct);
        var roles = roleByUser
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.First().Name);

        return ListEnvelope<StaffAccountDto>.Cursor(
            [.. items.Select(r => new StaffAccountDto(
                r.Id, r.Email!, r.FullName, roles.GetValueOrDefault(r.Id), r.IsActive,
                r.TwoFactorEnabled, r.LockoutEnd, r.ActiveSessions))],
            hasMore,
            hasMore ? new SupplierUserCursor(items[^1].Email!, items[^1].Id).Encode() : null,
            pageSize,
            totalCount,
            sort: "email");
    }
}

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

/// <summary>
/// T-077: deactivate or restore a staff account.
///
/// <para>Deactivation, never deletion - the same reasoning D-28 records for reference data, and more
/// strongly here: this account is the actor on audit rows, and an audit trail that points at a row
/// that no longer exists is not an audit trail. `IsActive = false` plus every session revoked is what
/// "removed" means for a person.</para>
/// </summary>
public sealed class SetStaffActiveHandler(
    AppDbContext db, UserManager<AppUser> userManager, IScopeContext scope, IAuditLogger auditLogger)
    : ISetStaffActiveHandler
{
    public async Task<StaffAccountResult> HandleAsync(Guid userId, bool isActive, CancellationToken ct)
    {
        var user = await StaffAccountLoader.LoadAsync(userManager, userId, ct);
        if (user is null) return new StaffAccountResult.NotFound();

        // Deactivating yourself is refused. Not paternalism: an administrator who does it is locked out
        // of the surface that would undo it, and if they were the last one nothing can.
        if (!isActive && user.Id == scope.UserId) return new StaffAccountResult.CannotActOnSelf();

        if (!isActive && await StaffAccountLoader.WouldLeaveNoAdministratorAsync(db, userManager, user, ct))
        {
            return new StaffAccountResult.WouldLockOutAdministration();
        }

        user.IsActive = isActive;
        await userManager.UpdateAsync(user);

        if (!isActive)
        {
            // Every live session dies with the account. Leaving them alive would make "deactivated" mean
            // "cannot sign in again", which is not what an administrator removing an account in error
            // needs it to mean.
            await db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow), ct);
        }

        await auditLogger.LogAsync("StaffAccount", user.Id,
            isActive ? "staff_reactivated" : "staff_deactivated", scope.UserId, ct: ct);
        await db.SaveChangesAsync(ct);

        return new StaffAccountResult.Success(await StaffAccountLoader.ToDtoAsync(db, userManager, user, ct));
    }
}

/// <summary>T-077/SCR-702: change which single role a staff account holds.</summary>
public sealed class ChangeStaffRoleHandler(
    AppDbContext db, UserManager<AppUser> userManager, IScopeContext scope, IAuditLogger auditLogger)
    : IChangeStaffRoleHandler
{
    /// <summary>The same set InviteStaffHandler will create, and for the same reason: a supplier role on
    /// an account with no SupplierId is a broken account.</summary>
    private static readonly HashSet<string> AssignableRoles =
    [
        Roles.OnboardingReviewer, Roles.ProcurementOfficer, Roles.ProcurementManager,
        Roles.Evaluator, Roles.MinistryViewer, Roles.SystemAdmin,
    ];

    public async Task<StaffAccountResult> HandleAsync(ChangeStaffRoleCommand command, CancellationToken ct)
    {
        if (!AssignableRoles.Contains(command.Role)) return new StaffAccountResult.NotFound();

        var user = await StaffAccountLoader.LoadAsync(userManager, command.UserId, ct);
        if (user is null) return new StaffAccountResult.NotFound();

        var current = await userManager.GetRolesAsync(user);
        if (current.Contains(command.Role))
        {
            return new StaffAccountResult.Success(await StaffAccountLoader.ToDtoAsync(db, userManager, user, ct));
        }

        // Demoting yourself out of system_admin is the same lockout as deactivating yourself, one step
        // less obvious.
        if (user.Id == scope.UserId && current.Contains(Roles.SystemAdmin) && command.Role != Roles.SystemAdmin)
        {
            return new StaffAccountResult.CannotActOnSelf();
        }

        if (command.Role != Roles.SystemAdmin
            && await StaffAccountLoader.WouldLeaveNoAdministratorAsync(db, userManager, user, ct))
        {
            return new StaffAccountResult.WouldLockOutAdministration();
        }

        // One role per staff account, which is what the invite creates and what the list reports. Removing
        // the old ones first keeps that true rather than accumulating roles nobody can see.
        if (current.Count > 0) await userManager.RemoveFromRolesAsync(user, current);
        await userManager.AddToRoleAsync(user, command.Role);

        // Sessions die: a permission set is stamped into the access token at sign-in (see D-30's note on
        // role claims), so a role change that left sessions alive would leave the OLD permissions in
        // force until they expired.
        await db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow), ct);

        await auditLogger.LogAsync("StaffAccount", user.Id, "staff_role_changed", scope.UserId,
            fromState: current.FirstOrDefault(), toState: command.Role, ct: ct);
        await db.SaveChangesAsync(ct);

        return new StaffAccountResult.Success(await StaffAccountLoader.ToDtoAsync(db, userManager, user, ct));
    }
}

/// <summary>
/// T-077/SCR-702: clear an authenticator enrolment.
///
/// <para>`system_admin` cannot hold a session without MFA, so an administrator who loses their
/// authenticator is locked out with no self-service path. This is that path, and it is deliberately an
/// administrator action on someone ELSE's account: a reset available to the holder would be a way past
/// the second factor.</para>
/// </summary>
public sealed class ResetStaffMfaHandler(
    AppDbContext db, UserManager<AppUser> userManager, IScopeContext scope, IAuditLogger auditLogger)
    : IResetStaffMfaHandler
{
    public async Task<StaffAccountResult> HandleAsync(Guid userId, CancellationToken ct)
    {
        var user = await StaffAccountLoader.LoadAsync(userManager, userId, ct);
        if (user is null) return new StaffAccountResult.NotFound();

        // Resetting your own MFA is refused: the point of the second factor is that possessing the first
        // one is not enough, and a session is the first one.
        if (user.Id == scope.UserId) return new StaffAccountResult.CannotActOnSelf();

        await userManager.SetTwoFactorEnabledAsync(user, false);
        await userManager.ResetAuthenticatorKeyAsync(user);

        // Every session revoked. A reset that left them alive would hand an attacker who already holds
        // one a way to stay past the very control being reset.
        await db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow), ct);

        await auditLogger.LogAsync("StaffAccount", user.Id, "staff_mfa_reset", scope.UserId, ct: ct);
        await db.SaveChangesAsync(ct);

        return new StaffAccountResult.Success(await StaffAccountLoader.ToDtoAsync(db, userManager, user, ct));
    }
}
