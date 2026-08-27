using Microsoft.AspNetCore.Identity;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>MSP-55: the opaque invite-link token is the sole lookup key (same scheme as
/// email-verification/password-reset - never a userId in the URL). Once resolved, a fresh
/// Identity reset token is generated and consumed internally to set the real password.</summary>
public sealed class AcceptSupplierUserInviteHandler(UserManager<AppUser> userManager, ISecurityTokenService securityTokenService) : IAcceptSupplierUserInviteHandler
{
    public async Task<AcceptSupplierUserInviteResult> HandleAsync(AcceptSupplierUserInviteCommand command, CancellationToken ct)
    {
        var consumed = await securityTokenService.ConsumeAsync(command.Token, SecurityTokenPurpose.SupplierUserInvite, ct);
        if (consumed is not ConsumeSecurityTokenResult.Success success)
        {
            return new AcceptSupplierUserInviteResult.InvalidOrExpiredToken();
        }

        var user = await userManager.FindByIdAsync(success.UserId.ToString());
        if (user is null)
        {
            return new AcceptSupplierUserInviteResult.InvalidOrExpiredToken();
        }

        var identityToken = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, identityToken, command.Password);
        if (!result.Succeeded)
        {
            return new AcceptSupplierUserInviteResult.WeakPassword([.. result.Errors.Select(e => e.Description)]);
        }

        return new AcceptSupplierUserInviteResult.Success();
    }
}
