// A system administrator invites a new member of ministry staff by email and role.
//
// It mirrors the supplier-side team invitation exactly: an unusable random password, the email treated as
// already confirmed because the invitation proved control of the inbox, the role assigned immediately, and the
// real password deferred to acceptance through the same opaque single-use token. The link never carries a user
// identifier.
//
// The token is minted inside the job rather than passed to it, so it never sits in the job store.
//
//
// THE SUPPLIER ROLES ARE DELIBERATELY NOT INVITABLE HERE
//
// Those accounts come from a supplier's own self-registration or their team invitation, both of which also
// stamp the company onto the account.
//
// An account made through this handler never gets a company, so mixing the two role families would produce a
// staff account holding a supplier-only role and scoped to no supplier at all.

namespace MotsSupplierPortal.Infrastructure.Identity;

using Hangfire;
using Microsoft.AspNetCore.Identity;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;

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

        var creation = await InviteUserCreation.CreateInvitedUserAsync(
            userManager, command.Email, command.FullName, supplierId: null, organizationId: command.OrganizationId);
        if (!creation.Succeeded)
        {
            return new InviteStaffResult.DuplicateEmail();
        }

        var user = creation.User!;
        await userManager.AddToRoleAsync(user, command.Role);

        backgroundJobs.Enqueue<EmailJobs>(job => job.SendStaffInviteEmailAsync(user.Id, CancellationToken.None));

        await auditLogger.LogAsync("AppUser", user.Id, "staff_invited", scope.UserId, toState: command.Role, ct: ct);
        await db.SaveChangesAsync(ct);

        return new InviteStaffResult.Success(new StaffDto(user.Id, user.Email!, user.FullName, command.Role));
    }
}
