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

    /// <summary>
    /// Splits the SHA-1 digest into the 5-character prefix sent to HIBP and the suffix compared
    /// locally. Extracted so the algorithm can be pinned by test - see the S4790 note in
    /// ValidateAsync for why it must stay SHA-1.
    /// </summary>
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
            // csharpsquid:S4790 ("using a weak hashing algorithm is security-sensitive") is a FALSE
            // POSITIVE here, and this comment - not the marking in SonarCloud - is the record of why.
            //
            // SHA-1 is mandated by the protocol, not chosen. The HIBP range API is a k-anonymity
            // scheme keyed on SHA-1: the client sends the first 5 hex characters of the SHA-1 digest
            // and gets back every known-breached suffix sharing that prefix. The algorithm is fixed
            // by the remote service. There is no SHA-256 variant of this endpoint to migrate to.
            //
            // Nothing here depends on SHA-1 being collision-resistant or preimage-resistant:
            //   - It is not used to store or verify a password. Storage is ASP.NET Core Identity's
            //     default PBKDF2 hasher; no IPasswordHasher is overridden anywhere in this solution,
            //     and this is the only SHA-1 call site in the codebase.
            //   - It is not used for authentication, signing, or integrity.
            //   - The digest is never persisted and never leaves this method except as its first
            //     5 characters, which by design match many millions of unrelated passwords.
            // A SHA-1 collision would, at worst, cause a breached password to be missed - the same
            // outcome as the fail-open path below, which is already the accepted behaviour.
            //
            // Do NOT "fix" this by switching to SHA-256. It would compile, pass every test, and
            // silently disable the control: the API would return SHA-1 suffixes that can never match
            // a SHA-256 one, so every password would validate as clean. That is a security check
            // that reports success while doing nothing - the failure mode this codebase keeps
            // finding. HashForRangeQuery below is pinned by a test against HIBP's own published
            // vector so that substitution fails loudly instead.
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
