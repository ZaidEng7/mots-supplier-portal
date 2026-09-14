// Refusing passwords that are known to have appeared in a public breach.
//
// It uses the public breach service's anonymised range lookup, so only the first five characters of a hash
// ever leave the server, never the password.
//
// It can be switched off in configuration, which is what keeps the test suite hermetic and free of an external
// network dependency.
//
//
// IT FAILS OPEN
//
// A timeout, or the service being unreachable, logs a warning and admits the password.
//
// A breach check that could block registration and password reset outright would turn an external dependency
// into a single point of failure for a control that is security-adjacent rather than critical.
//
//
// THE HASH IS THE PROTOCOL'S, NOT A CHOICE, AND MUST NOT BE "UPGRADED"
//
// A static analyser flags the older hash algorithm as security-sensitive. That is a false positive here, and
// this paragraph, rather than a marking in the analyser's own dashboard, is the record of why.
//
// The remote lookup IS an anonymity scheme keyed on that algorithm: the client sends the first five hex
// characters of the digest and receives every known-breached suffix sharing that prefix. The algorithm is
// fixed by the remote service, and there is no variant of the endpoint keyed on a newer one to migrate to.
//
// Nothing here depends on the algorithm resisting collisions or inversion. It does not store or verify a
// password, because storage is the identity framework's own key-derivation hasher and nothing in this solution
// overrides it; this is the only call site of the older algorithm in the codebase. It is not used for
// authentication, signing or integrity. The digest is never persisted and never leaves this method except as
// those first five characters, which by design match many millions of unrelated passwords.
//
// A collision would, at worst, let a breached password through, which is the same outcome as the fail-open path
// that is already the accepted behaviour.
//
// Do NOT "fix" this by switching to a newer hash. It would compile, pass every test, and silently disable the
// control: the service would return suffixes of the old digest that can never match a new one, so every
// password would validate as clean. That is a security check reporting success while doing nothing, which is
// the failure mode this codebase keeps finding.
//
// The hashing step is extracted so a test can pin it against the service's own published example, which is
// what makes such a substitution fail loudly instead.

namespace MotsSupplierPortal.Infrastructure.Identity;

using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MotsSupplierPortal.Domain.Identity;

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

    public static (string Prefix, string Suffix) HashForRangeQuery(string password)
    {
        var digest = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
        return (digest[..5], digest[5..]);
    }

    public async Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user, string? password)
    {
        if (!configuration.GetValue("Password:BreachCheckEnabled", true) || string.IsNullOrEmpty(password))
        {
            return IdentityResult.Success;
        }

        try
        {
            var (prefix, suffix) = HashForRangeQuery(password);

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
