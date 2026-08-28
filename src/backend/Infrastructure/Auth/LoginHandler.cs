using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Auth;

/// <summary>
/// STORY-01.1.1: email+password login. Issues JWT access + rotating refresh token (hashed,
/// bound to a token family). No user-enumeration on any failure path.
/// </summary>
public sealed class LoginHandler(
    AppDbContext db,
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    IJwtTokenService jwtTokenService,
    PermissionResolver permissionResolver,
    IAuditLogger auditLogger,
    IOptions<JwtOptions> jwtOptions,
    IConfiguration configuration) : ILoginHandler
{
    private readonly JwtOptions _jwtOptions = jwtOptions.Value;

    /// <summary>NFR-SEC-003 mandates MFA for system_admin at minimum. Configurable so the list can
    /// widen (e.g. procurement_manager per FR-IAM-004) without a code change.</summary>
    private readonly string[] _mfaRequiredRoles =
        configuration.GetSection("Mfa:RequiredRoles").Get<string[]>() ?? [Roles.SystemAdmin];

    public async Task<LoginResult> HandleAsync(LoginCommand command, CancellationToken ct)
    {
        var user = await userManager.FindByEmailAsync(command.Email.Trim().ToLowerInvariant());
        if (user is null)
        {
            // Constant-shape failure: no user-enumeration (STORY-01.1.1 AC2).
            return new LoginResult.InvalidCredentials();
        }

        var checkResult = await signInManager.CheckPasswordSignInAsync(user, command.Password, lockoutOnFailure: true);

        if (checkResult.IsLockedOut)
        {
            await auditLogger.LogAsync("User", user.Id, "login_locked_out", Guid.NewGuid(), user.Id, user.FullName, ct: ct);
            return new LoginResult.LockedOut();
        }

        if (!checkResult.Succeeded)
        {
            await auditLogger.LogAsync("User", user.Id, "login_failed", Guid.NewGuid(), user.Id, user.FullName, ct: ct);
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

        // MSP-67 / FR-IAM-004 / NFR-SEC-003: the second factor is enforced HERE, at the point a
        // session is issued. Before this, enrolment existed but login never consulted it, so an
        // enrolled user still authenticated with a password alone - worse than no MFA, because it
        // presented assurance it did not provide.
        var roles = await userManager.GetRolesAsync(user);
        var mfaMandatoryForRole = roles.Any(RequiresMfa);

        if (mfaMandatoryForRole && !user.TwoFactorEnabled)
        {
            await auditLogger.LogAsync("User", user.Id, "login_blocked_mfa_enrollment_required", Guid.NewGuid(), user.Id, user.FullName, ct: ct);
            return new LoginResult.MfaEnrollmentRequired();
        }

        var factors = new List<string> { "pwd" };

        if (user.TwoFactorEnabled)
        {
            if (string.IsNullOrWhiteSpace(command.TotpCode))
            {
                // Deliberately NOT an audit "failure" - the password leg succeeded; this is a
                // normal challenge, not a rejected attempt.
                return new LoginResult.MfaRequired();
            }

            if (!await VerifySecondFactorAsync(user, command.TotpCode))
            {
                await auditLogger.LogAsync("User", user.Id, "login_mfa_failed", Guid.NewGuid(), user.Id, user.FullName, ct: ct);
                return new LoginResult.MfaInvalid();
            }

            factors.Add("otp");
        }

        var tokens = await IssueTokenPairAsync(user, familyId: Guid.CreateVersion7(), command.Ip, command.UserAgent, ct, factors);

        await auditLogger.LogAsync("User", user.Id, "login_succeeded", Guid.NewGuid(), user.Id, user.FullName, ct: ct);

        return new LoginResult.Success(tokens);
    }

    /// <summary>Accepts either a live TOTP code or a single-use recovery code, so a user who has
    /// lost their authenticator is not locked out (STORY-01.5.1 AC3). Brute force is bounded by the
    /// login endpoint's existing per-IP and per-account rate limiters, which this path shares.</summary>
    private async Task<bool> VerifySecondFactorAsync(AppUser user, string code)
    {
        var normalized = code.Replace(" ", string.Empty).Replace("-", string.Empty);

        if (await userManager.VerifyTwoFactorTokenAsync(user, userManager.Options.Tokens.AuthenticatorTokenProvider, normalized))
        {
            return true;
        }

        var recovery = await userManager.RedeemTwoFactorRecoveryCodeAsync(user, normalized);
        return recovery.Succeeded;
    }

    private bool RequiresMfa(string role) =>
        _mfaRequiredRoles.Contains(role, StringComparer.OrdinalIgnoreCase);

    /// <summary><paramref name="authMethods"/> defaults to password-only for the refresh path,
    /// which re-issues against an already-established session rather than re-authenticating.</summary>
    internal async Task<TokenPair> IssueTokenPairAsync(AppUser user, Guid familyId, string? ip, string? userAgent, CancellationToken ct, IReadOnlyList<string>? authMethods = null)
    {
        var permissions = await permissionResolver.ResolveAsync(user);
        var roles = (IReadOnlyList<string>)await userManager.GetRolesAsync(user);
        // amr = "authentication methods reference" (SECURITY-ARCHITECTURE §1.1/§1.5): reflects the
        // factors actually used on this login, so a step-up policy can distinguish a
        // password-only session from an MFA-verified one.
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
