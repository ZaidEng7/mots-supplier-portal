using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Infrastructure.Auth;
using MotsSupplierPortal.Infrastructure.Configuration;

namespace MotsSupplierPortal.Infrastructure.Admin;

/// <summary>
/// SCR-726's read: the security policy this deployment is actually running.
///
/// <para><b>Every value is read from the thing that ENFORCES it, or from the one key that configures it.</b>
/// The password and lockout rules come out of the configured IdentityOptions, the MFA list out of the
/// expression LoginHandler itself uses, the registration mode out of the settings reader the registration
/// endpoint consults, and the clock skew out of the configuration key Program.cs now builds the token
/// validation parameters from. A
/// screen that restated any of these as its own literals would be a screen that keeps reporting the old
/// policy after someone changes the real one - which is worse than having no screen, because it would be
/// believed.</para>
/// </summary>
public sealed class SecurityPostureHandler(
    IOptions<IdentityOptions> identityOptions,
    IConfiguration configuration,
    ISystemSettingReader settings) : IGetSecurityPostureHandler
{
    public async Task<SecurityPostureDto> HandleAsync(CancellationToken ct)
    {
        var identity = identityOptions.Value;

        return new SecurityPostureDto(
            new PasswordPolicyDto(
                identity.Password.RequiredLength,
                identity.Password.RequireDigit,
                identity.Password.RequireUppercase,
                identity.Password.RequireLowercase,
                identity.Password.RequireNonAlphanumeric),
            new LockoutPolicyDto(
                identity.Lockout.MaxFailedAccessAttempts,
                (int)identity.Lockout.DefaultLockoutTimeSpan.TotalMinutes),
            new SessionPolicyDto(
                configuration.GetValue("Jwt:AccessTokenMinutes", 15),
                configuration.GetValue("Jwt:RefreshTokenDays", 30),
                // The skew is why a "15 minute" token is not one, and it is the kind of thing nobody
                // remembers is configured. Program.cs now reads the SAME key when it builds the token
                // validation parameters, so this cannot report a number the handler is not using.
                configuration.GetValue("Jwt:ClockSkewSeconds", 30)),
            MfaPolicy.RequiredRoles(configuration),
            [
                // The two limiters that exist, named as their policies are named in Program.cs so an
                // operator reading a 429 can find the row it came from.
                new RateLimitPolicyDto("auth-strict", configuration.GetValue("RateLimiting:AuthPermitLimit", 10), 60),
                new RateLimitPolicyDto("register-strict", configuration.GetValue("RateLimiting:RegisterPermitLimit", 5), 60),
            ],
            await settings.GetAsync(SystemSettings.RegistrationMode, ct));
    }
}
