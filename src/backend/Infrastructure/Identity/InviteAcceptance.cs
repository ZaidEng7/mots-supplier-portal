// Accepting an invitation: the part both the staff flow and the supplier flow share.
//
// The opaque single-use token is consumed by purpose and is the only lookup key. The link never carries a user
// identifier.
//
// The real password is then set through the identity framework's own reset flow, with a token generated and
// consumed internally.
//
// Only the purpose and each caller's own result shape differ between the two flows. Everything else was a
// byte-for-byte duplicate before this was extracted.

namespace MotsSupplierPortal.Infrastructure.Identity;

using Microsoft.AspNetCore.Identity;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;

public enum AcceptInviteOutcome
{
    Success,
    InvalidOrExpiredToken,
    WeakPassword,
}

public sealed record AcceptInviteCoreResult(AcceptInviteOutcome Outcome, IReadOnlyList<string> Errors);

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
