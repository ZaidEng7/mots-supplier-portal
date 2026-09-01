using Hangfire;
using Microsoft.AspNetCore.Identity;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Identity;

/// <summary>
/// Task #28/FR-ADM-001: system_admin invites a new staff account by email + role. Mirrors
/// InviteSupplierUserHandler exactly - unusable random password, EmailConfirmed=true, role
/// assigned immediately, real password deferred to AcceptStaffInviteHandler via the same opaque
/// SecurityToken scheme (never a userId in the invite link).
///
/// <para>Deliberately excludes supplier_admin/supplier_user from the invitable role set: those
/// accounts come from supplier self-registration or the supplier-side team invite
/// (InviteSupplierUserHandler), which also stamp SupplierId - an account made through THIS handler
/// never gets a SupplierId, so mixing the two role families here would produce a staff account
/// that is also (incorrectly) scoped to no supplier while holding a supplier-only role.</para>
/// </summary>
public sealed class InviteStaffHandler(
    AppDbContext db,
    UserManager<AppUser> userManager,
    IScopeContext scope,
    IAuditLogger auditLogger,
    IBackgroundJobClient backgroundJobs) : IInviteStaffHandler
{
    private static readonly HashSet<string> InvitableRoles =
    [
        Roles.OnboardingReviewer,
        Roles.ProcurementOfficer,
        Roles.ProcurementManager,
        Roles.Evaluator,
        Roles.MinistryViewer,
        Roles.SystemAdmin,
    ];

    public async Task<InviteStaffResult> HandleAsync(InviteStaffCommand command, CancellationToken ct)
    {
        if (!InvitableRoles.Contains(command.Role))
        {
            return new InviteStaffResult.InvalidRole();
        }

        // Unusable random password - the account only becomes usable once the invite is accepted
        // and a real password is set via AcceptStaffInviteHandler.
        var creation = await InviteUserCreation.CreateInvitedUserAsync(userManager, command.Email, command.FullName, supplierId: null);
        if (!creation.Succeeded)
        {
            return new InviteStaffResult.DuplicateEmail();
        }

        var user = creation.User!;
        await userManager.AddToRoleAsync(user, command.Role);

        // Token minted inside the job (MSP-89 pattern) - see EmailJobs's own doc comment.
        backgroundJobs.Enqueue<EmailJobs>(job => job.SendStaffInviteEmailAsync(user.Id, CancellationToken.None));

        await auditLogger.LogAsync("AppUser", user.Id, "staff_invited", scope.UserId, toState: command.Role, ct: ct);
        await db.SaveChangesAsync(ct);

        return new InviteStaffResult.Success(new StaffDto(user.Id, user.Email!, user.FullName, command.Role));
    }
}
