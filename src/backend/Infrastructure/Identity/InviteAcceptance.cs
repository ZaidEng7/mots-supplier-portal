using Microsoft.AspNetCore.Identity;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Infrastructure.Identity;

public enum AcceptInviteOutcome
{
    Success,
    InvalidOrExpiredToken,
    WeakPassword,
}

public sealed record AcceptInviteCoreResult(AcceptInviteOutcome Outcome, IReadOnlyList<string> Errors);

/// <summary>
/// Shared by AcceptSupplierUserInviteHandler and AcceptStaffInviteHandler (and any future invite
/// flow that follows the same shape): consume an opaque SecurityToken by purpose - the sole
/// lookup key, never a userId in the URL (SECURITY-ARCHITECTURE.md 1.6/1.7) - then set the real
/// password via ASP.NET Core Identity's own reset-token flow. Only the SecurityTokenPurpose and
/// each caller's own result-type wrapping differ between the two flows; everything else was a
/// byte-for-byte duplicate before this extraction.
/// </summary>
public static class InviteAcceptance
{
    public static async Task<AcceptInviteCoreResult> AcceptAsync(
        UserManager<AppUser> userManager,
        ISecurityTokenService securityTokenService,
        string rawToken,
        SecurityTokenPurpose purpose,
        string password,
        CancellationToken ct)
    {
        var consumed = await securityTokenService.ConsumeAsync(rawToken, purpose, ct);
        if (consumed is not ConsumeSecurityTokenResult.Success success)
        {
            return new AcceptInviteCoreResult(AcceptInviteOutcome.InvalidOrExpiredToken, []);
        }

        var user = await userManager.FindByIdAsync(success.UserId.ToString());
        if (user is null)
        {
            return new AcceptInviteCoreResult(AcceptInviteOutcome.InvalidOrExpiredToken, []);
        }

        var identityToken = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, identityToken, password);
        if (!result.Succeeded)
        {
            return new AcceptInviteCoreResult(AcceptInviteOutcome.WeakPassword, [.. result.Errors.Select(e => e.Description)]);
        }

        return new AcceptInviteCoreResult(AcceptInviteOutcome.Success, []);
    }
}
