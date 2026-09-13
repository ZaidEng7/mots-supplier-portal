// Signing in with an email address and a password, and issuing the session.
//
// It issues an access token plus a rotating refresh token, stored only as a hash and bound to a token family.
//
// No failure path reveals whether an account exists: an unknown address and a wrong password give the same
// answer in the same shape.
//
//
// THE SECOND FACTOR IS ENFORCED HERE, WHERE THE SESSION IS ISSUED
//
// Before this, enrolment existed and sign-in never consulted it, so an enrolled user still authenticated with a
// password alone. That is worse than no second factor, because it presented assurance it did not provide.
//
// A role that requires a second factor and an account that has not enrolled one is refused rather than let
// through, and the refusal says what to do.
//
// The list of roles that require it is shared with the security-posture screen that reports it, so there are two
// readers of one expression rather than two copies of one default.
//
// A missing code on an enrolled account is deliberately not audited as a failure. The password leg succeeded;
// this is a normal challenge rather than a rejected attempt.
//
// Either a live code or a single-use recovery code is accepted, so a user who has lost their authenticator is
// not locked out. Brute force is bounded by the endpoint's existing per-address and per-account rate limits,
// which this path shares.
//
//
// WHAT THE TOKEN RECORDS ABOUT HOW YOU SIGNED IN
//
// The token carries the factors actually used, so a policy that wants to step up can tell a password-only
// session from one verified with a second factor.
//
// The refresh path re-issues against an already-established session rather than re-authenticating, so it
// defaults to password-only rather than claiming a factor it did not see.

namespace MotsSupplierPortal.Infrastructure.Auth;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class LoginHandler(
    AppDbContext db,
    IIdentityProvider identityProvider,
    IJwtTokenService jwtTokenService,
    PermissionResolver permissionResolver,
    IAuditLogger auditLogger,
    IOptions<JwtOptions> jwtOptions,
    IConfiguration configuration) : ILoginHandler
{
    private readonly JwtOptions _jwtOptions = jwtOptions.Value;

    private readonly string[] _mfaRequiredRoles = MfaPolicy.RequiredRoles(configuration);

    public async Task<LoginResult> HandleAsync(LoginCommand command, CancellationToken ct)
    {
        var user = await identityProvider.FindByEmailAsync(command.Email.Trim().ToLowerInvariant());
        if (user is null)
        {
            return new LoginResult.InvalidCredentials();
        }

        var checkResult = await identityProvider.CheckPasswordSignInAsync(user, command.Password, lockoutOnFailure: true);

        if (checkResult.IsLockedOut)
        {
            await auditLogger.LogAsync("User", user.Id, "login_locked_out", user.Id, user.FullName, ct: ct);
            return new LoginResult.LockedOut();
        }

        if (!checkResult.Succeeded)
        {
            await auditLogger.LogAsync("User", user.Id, "login_failed", user.Id, user.FullName, ct: ct);
            return new LoginResult.InvalidCredentials();
        }

        if (!user.IsActive)
        {
            return new LoginResult.AccountNotUsable("account_disabled");
        }

        if (!user.EmailConfirmed)
        {
            return new LoginResult.AccountNotUsable("email_not_verified");
        }

        var roles = await identityProvider.GetRolesAsync(user);
        var mfaMandatoryForRole = roles.Any(RequiresMfa);

        if (mfaMandatoryForRole && !user.TwoFactorEnabled)
        {
            await auditLogger.LogAsync("User", user.Id, "login_blocked_mfa_enrollment_required", user.Id, user.FullName, ct: ct);
            return new LoginResult.MfaEnrollmentRequired();
        }

        var factors = new List<string> { "pwd" };

        if (user.TwoFactorEnabled)
        {
            if (string.IsNullOrWhiteSpace(command.TotpCode))
            {
                return new LoginResult.MfaRequired();
            }

            if (!await VerifySecondFactorAsync(user, command.TotpCode))
            {
                await auditLogger.LogAsync("User", user.Id, "login_mfa_failed", user.Id, user.FullName, ct: ct);
                return new LoginResult.MfaInvalid();
            }

            factors.Add("otp");
        }

        var tokens = await IssueTokenPairAsync(user, familyId: Guid.CreateVersion7(), command.Ip, command.UserAgent, ct, factors);

        await auditLogger.LogAsync("User", user.Id, "login_succeeded", user.Id, user.FullName, ct: ct);

        return new LoginResult.Success(tokens);
    }

    private async Task<bool> VerifySecondFactorAsync(AppUser user, string code)
    {
        var normalized = code.Replace(" ", string.Empty).Replace("-", string.Empty);

        if (await identityProvider.VerifyTwoFactorTokenAsync(user, normalized))
        {
            return true;
        }

        return await identityProvider.RedeemTwoFactorRecoveryCodeAsync(user, normalized);
    }

    private bool RequiresMfa(string role) =>
        _mfaRequiredRoles.Contains(role, StringComparer.OrdinalIgnoreCase);

    internal async Task<TokenPair> IssueTokenPairAsync(AppUser user, Guid familyId, string? ip, string? userAgent, CancellationToken ct, IReadOnlyList<string>? authMethods = null)
    {
        var permissions = await permissionResolver.ResolveAsync(user);
        var roles = await identityProvider.GetRolesAsync(user);
        var access = jwtTokenService.IssueAccessToken(user.Id, user.Email!, user.SupplierId, user.OrganizationId, roles, permissions, authMethods ?? ["pwd"]);

        var refreshPlainText = TokenHasher.GenerateOpaqueToken();
        db.RefreshTokens.Add(new Domain.Identity.RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            TokenHash = TokenHasher.Hash(refreshPlainText),
            FamilyId = familyId,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_jwtOptions.RefreshTokenDays),
            Ip = ip,
            UserAgent = userAgent,
        });
        await db.SaveChangesAsync(ct);

        return new TokenPair(access.Token, access.ExpiresAt, refreshPlainText);
    }
}
