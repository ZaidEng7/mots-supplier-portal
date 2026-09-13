using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Identity;

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
