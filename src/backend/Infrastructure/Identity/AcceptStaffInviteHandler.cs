using Microsoft.AspNetCore.Identity;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Infrastructure.Identity;

/// <summary>Task #28: mirrors AcceptSupplierUserInviteHandler exactly - the opaque invite-link
/// token is the sole lookup key (never a userId in the URL); once resolved, a fresh Identity
/// reset token is generated and consumed internally to set the real password.</summary>
public sealed class AcceptStaffInviteHandler(UserManager<AppUser> userManager, ISecurityTokenService securityTokenService) : IAcceptStaffInviteHandler
{
    public async Task<AcceptStaffInviteResult> HandleAsync(AcceptStaffInviteCommand command, CancellationToken ct)
    {
        var consumed = await securityTokenService.ConsumeAsync(command.Token, SecurityTokenPurpose.StaffInvite, ct);
        if (consumed is not ConsumeSecurityTokenResult.Success success)
        {
            return new AcceptStaffInviteResult.InvalidOrExpiredToken();
        }

        var user = await userManager.FindByIdAsync(success.UserId.ToString());
        if (user is null)
        {
            return new AcceptStaffInviteResult.InvalidOrExpiredToken();
        }

        var identityToken = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, identityToken, command.Password);
        if (!result.Succeeded)
        {
            return new AcceptStaffInviteResult.WeakPassword([.. result.Errors.Select(e => e.Description)]);
        }

        return new AcceptStaffInviteResult.Success();
    }
}
