using Microsoft.AspNetCore.Identity;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Identity;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>MSP-55: the opaque invite-link token is the sole lookup key (same scheme as
/// email-verification/password-reset - never a userId in the URL). Once resolved, a fresh
/// Identity reset token is generated and consumed internally to set the real password - the
/// actual work is InviteAcceptance.AcceptAsync, shared with AcceptStaffInviteHandler; this class
/// only maps that shared outcome onto this flow's own result type.</summary>
public sealed class AcceptSupplierUserInviteHandler(UserManager<AppUser> userManager, ISecurityTokenService securityTokenService) : IAcceptSupplierUserInviteHandler
{
    public async Task<AcceptSupplierUserInviteResult> HandleAsync(AcceptSupplierUserInviteCommand command, CancellationToken ct)
    {
        var core = await InviteAcceptance.AcceptAsync(
            userManager, securityTokenService, command.Token, SecurityTokenPurpose.SupplierUserInvite, command.Password, ct);

        return core.Outcome switch
        {
            AcceptInviteOutcome.Success => new AcceptSupplierUserInviteResult.Success(),
            AcceptInviteOutcome.WeakPassword => new AcceptSupplierUserInviteResult.WeakPassword(core.Errors),
            _ => new AcceptSupplierUserInviteResult.InvalidOrExpiredToken(),
        };
    }
}
