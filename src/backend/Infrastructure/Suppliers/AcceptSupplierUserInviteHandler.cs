// A colleague accepts their invitation into a supplier's account and sets a password.
//
// The opaque token from the link is the only lookup key, the same scheme verification and password reset
// use. The link never carries a user identifier.
//
// Once resolved, a fresh reset token is generated and consumed internally to set the real password. The
// actual work is shared with the staff invitation flow; this class only maps that shared outcome onto this
// flow's own result type.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.AspNetCore.Identity;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Identity;

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
