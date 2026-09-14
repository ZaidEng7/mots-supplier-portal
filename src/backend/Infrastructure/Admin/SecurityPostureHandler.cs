// The security posture screen: the policy this deployment is actually running.
//
// Every value is read from the thing that ENFORCES it, or from the one key that configures it.
//
// The password and lockout rules come out of the configured identity options. The second-factor list comes out of
// the same expression the sign-in handler uses. The registration mode comes from the settings reader the
// registration endpoint consults. The clock skew comes from the configuration key the token validation is built
// from.
//
// A screen restating any of these as its own literals would keep reporting the old policy after somebody changed
// the real one, which is worse than having no screen because it would be believed.
//
// The clock skew is why a fifteen-minute token is not one, and it is the kind of thing nobody remembers is
// configured, which is why it is on the screen at all.
//
// The rate limits are named exactly as their policies are named at registration, so an operator reading a
// refused request can find the row it came from.

namespace MotsSupplierPortal.Infrastructure.Admin;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Infrastructure.Auth;
using MotsSupplierPortal.Infrastructure.Configuration;

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
                configuration.GetValue("Jwt:ClockSkewSeconds", 30)),
            MfaPolicy.RequiredRoles(configuration),
            [
                new RateLimitPolicyDto("auth-strict", configuration.GetValue("RateLimiting:AuthPermitLimit", 10), 60),
                new RateLimitPolicyDto("register-strict", configuration.GetValue("RateLimiting:RegisterPermitLimit", 5), 60),
            ],
            await settings.GetAsync(SystemSettings.RegistrationMode, ct));
    }
}
