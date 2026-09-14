// Changing which single role a staff account holds.
//
// The assignable set is the same one the invitation creates, and for the same reason: a supplier role on an
// account with no company is a broken account.
//
// One role per staff account, which is what the invitation creates and what the list reports. The old roles are
// removed first, which keeps that true rather than accumulating roles nobody can see.
//
//
// TWO LOCKOUTS IT REFUSES
//
// Demoting yourself out of the administrator role is the same lockout as deactivating yourself, one step less
// obvious.
//
// And a change that would leave the system with no administrator at all is refused outright.
//
//
// THE ACCOUNT'S LIVE SESSIONS END
//
// A permission set is stamped into the access token at sign-in, so a role change that left sessions alive would
// leave the OLD permissions in force until they expired.

namespace MotsSupplierPortal.Infrastructure.Identity;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class ChangeStaffRoleHandler(
    AppDbContext db, UserManager<AppUser> userManager, IScopeContext scope, IAuditLogger auditLogger)
    : IChangeStaffRoleHandler
{
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

        if (user.Id == scope.UserId && current.Contains(Roles.SystemAdmin) && command.Role != Roles.SystemAdmin)
        {
            return new StaffAccountResult.CannotActOnSelf();
        }

        if (command.Role != Roles.SystemAdmin
            && await StaffAccountLoader.WouldLeaveNoAdministratorAsync(db, userManager, user, ct))
        {
            return new StaffAccountResult.WouldLockOutAdministration();
        }

        if (current.Count > 0) await userManager.RemoveFromRolesAsync(user, current);
        await userManager.AddToRoleAsync(user, command.Role);

        await db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow), ct);

        await auditLogger.LogAsync("StaffAccount", user.Id, "staff_role_changed", scope.UserId,
            fromState: current.FirstOrDefault(), toState: command.Role, ct: ct);
        await db.SaveChangesAsync(ct);

        return new StaffAccountResult.Success(await StaffAccountLoader.ToDtoAsync(db, userManager, user, ct));
    }
}
