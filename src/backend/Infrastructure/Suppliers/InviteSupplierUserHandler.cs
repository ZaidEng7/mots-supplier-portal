using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>FEAT-04.8/FR-PROF-008/MSP-55 AC1: supplier_admin invites a supplier_user scoped to
/// their own SupplierId. The new user is created with an unusable random password and
/// EmailConfirmed=true (the invite itself, sent to a real address, proves control of the inbox -
/// no separate verification step needed on top of accepting the invite); a real password is set
/// only via AcceptSupplierUserInviteHandler, gated by the same opaque SecurityToken scheme as
/// email-verification/password-reset (never a userId in the invite link).</summary>
public sealed class InviteSupplierUserHandler(
    // Re-added deliberately. This parameter was removed as unread when warnings became errors
    // (CS9113); it is needed again now that AuditLogger no longer owns the save. The warning was
    // correct at the time - the dependency really was unused - which is worth noting rather than
    // reading as churn.
    AppDbContext db,
    UserManager<AppUser> userManager,
    IScopeContext scope,
    IAuditLogger auditLogger,
    IBackgroundJobClient backgroundJobs) : IInviteSupplierUserHandler
{
    public async Task<InviteSupplierUserResult> HandleAsync(InviteSupplierUserCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new InviteSupplierUserResult.NotFoundOrOutOfScope();

        // Unusable random password - the account only becomes usable once the invite is accepted
        // and a real password is set via AcceptSupplierUserInviteHandler.
        var creation = await MotsSupplierPortal.Infrastructure.Identity.InviteUserCreation.CreateInvitedUserAsync(
            userManager, command.Email, command.FullName, scope.SupplierId);
        if (!creation.Succeeded)
        {
            return new InviteSupplierUserResult.DuplicateEmail();
        }

        var user = creation.User!;
        await userManager.AddToRoleAsync(user, Roles.SupplierUser);

        // Token minted inside the job (MSP-89) - see ForgotPasswordHandler for why.
        backgroundJobs.Enqueue<EmailJobs>(job => job.SendSupplierUserInviteEmailAsync(user.Id, CancellationToken.None));

        await auditLogger.LogAsync("Supplier", scope.SupplierId.Value, "supplier_user_invited", scope.UserId, ct: ct);
        // MSP-64: UserManager persists the new user; the audit row is on the AppDbContext and
        // AuditLogger no longer saves. Without this, a user would be granted access to a
        // supplier with no record of who invited them.
        await db.SaveChangesAsync(ct);

        return new InviteSupplierUserResult.Success(new SupplierUserDto(user.Id, user.Email!, user.FullName, user.IsActive));
    }
}
