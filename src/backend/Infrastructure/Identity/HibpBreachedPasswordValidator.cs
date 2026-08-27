using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Infrastructure.Identity;

/// <summary>
/// SECURITY-ARCHITECTURE.md §1.4/FR-IAM-003 (Must-have): rejects known-breached passwords via the
/// HIBP k-anonymity range API - only a 5-char SHA-1 prefix ever leaves the server, never the
/// password itself. Fails OPEN on any network/HTTP error (timeout, HIBP unreachable): a breach
/// check that can block registration/reset outright would turn an external dependency into a
/// single point of failure for a security-adjacent but non-critical control - logged as a warning
/// instead. Disable entirely via config ("Password:BreachCheckEnabled": false) - used by the
/// integration test fixture to keep CI hermetic (no external network dependency in tests).
/// </summary>
public sealed class HibpBreachedPasswordValidator(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<HibpBreachedPasswordValidator> logger) : IPasswordValidator<AppUser>
{
    private static readonly IdentityError BreachedError = new()
    {
        Code = "BreachedPassword",
        Description = "This password has appeared in a known data breach. Please choose a different one.",
    };

    public async Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user, string? password)
    {
        if (!configuration.GetValue("Password:BreachCheckEnabled", true) || string.IsNullOrEmpty(password))
        {
            return IdentityResult.Success;
        }

        try
        {
            var sha1 = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
            var prefix = sha1[..5];
            var suffix = sha1[5..];

            var client = httpClientFactory.CreateClient(nameof(HibpBreachedPasswordValidator));
            client.Timeout = TimeSpan.FromSeconds(3);
            using var response = await client.GetAsync($"https://api.pwnedpasswords.com/range/{prefix}");
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("HIBP breach check returned {StatusCode}; failing open", response.StatusCode);
                return IdentityResult.Success;
            }

            var body = await response.Content.ReadAsStringAsync();
            var isBreached = body
                .Split('\n')
                .Select(line => line.Split(':'))
                .Any(parts => parts.Length == 2 && parts[0].Trim().Equals(suffix, StringComparison.OrdinalIgnoreCase));

            return isBreached ? IdentityResult.Failed(BreachedError) : IdentityResult.Success;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "HIBP breach check unreachable; failing open");
            return IdentityResult.Success;
        }
    }
}
