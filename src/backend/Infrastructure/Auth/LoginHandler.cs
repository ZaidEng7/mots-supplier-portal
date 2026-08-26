using Microsoft.AspNetCore.Identity;
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
    IOptions<JwtOptions> jwtOptions) : ILoginHandler
{
    private readonly JwtOptions _jwtOptions = jwtOptions.Value;

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

        var tokens = await IssueTokenPairAsync(user, familyId: Guid.CreateVersion7(), command.Ip, command.UserAgent, ct);

        await auditLogger.LogAsync("User", user.Id, "login_succeeded", Guid.NewGuid(), user.Id, user.FullName, ct: ct);

        return new LoginResult.Success(tokens);
    }

    internal async Task<TokenPair> IssueTokenPairAsync(AppUser user, Guid familyId, string? ip, string? userAgent, CancellationToken ct)
    {
        var permissions = await permissionResolver.ResolveAsync(user);
        var access = jwtTokenService.IssueAccessToken(user.Id, user.Email!, user.SupplierId, user.OrganizationId, permissions);

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
