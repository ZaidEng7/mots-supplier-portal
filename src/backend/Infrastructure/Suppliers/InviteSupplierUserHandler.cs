// A supplier administrator invites a colleague into their own company's account.
//
// The new account is created with an unusable random password and its email already treated as confirmed:
// the invitation itself was sent to a real address, which proves control of the inbox, so a separate
// verification step on top of accepting the invitation adds nothing.
//
// A real password is set only by accepting the invitation, through the same opaque single-use token scheme
// as verification and password reset. The link never carries a user identifier.
//
// The token is minted inside the background job rather than passed to it, for the reason the forgot-password
// handler explains: a token in a job argument sits in the job store.
//
// The database context is a dependency again. It was removed as unread when warnings became errors, and is
// needed once more now that the audit logger no longer saves on its own. The warning was correct at the
// time, which is worth saying rather than reading as churn.
//
// Without that save the new user would be granted access to a supplier with no record of who invited them:
// the account is persisted by the user manager, but the audit row is on this context.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class InviteSupplierUserHandler(
    AppDbContext db,
    UserManager<AppUser> userManager,
    IScopeContext scope,
    IAuditLogger auditLogger,
    IBackgroundJobClient backgroundJobs) : IInviteSupplierUserHandler
{
    public async Task<InviteSupplierUserResult> HandleAsync(InviteSupplierUserCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new InviteSupplierUserResult.NotFoundOrOutOfScope();

        var creation = await MotsSupplierPortal.Infrastructure.Identity.InviteUserCreation.CreateInvitedUserAsync(
            userManager, command.Email, command.FullName, scope.SupplierId);
        if (!creation.Succeeded)
        {
            return new InviteSupplierUserResult.DuplicateEmail();
        }

        var user = creation.User!;
        await userManager.AddToRoleAsync(user, Roles.SupplierUser);

        backgroundJobs.Enqueue<EmailJobs>(job => job.SendSupplierUserInviteEmailAsync(user.Id, CancellationToken.None));

        await auditLogger.LogAsync("Supplier", scope.SupplierId.Value, "supplier_user_invited", scope.UserId, ct: ct);
        await db.SaveChangesAsync(ct);

        return new InviteSupplierUserResult.Success(new SupplierUserDto(user.Id, user.Email!, user.FullName, user.IsActive));
    }
}
