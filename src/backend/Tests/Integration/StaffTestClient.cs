using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// A logged-in staff (back-office) identity, for endpoints guarded by a staff permission.
///
/// This did not exist before MSP-63 - FlaggedFieldEnforcementTests records the gap explicitly and
/// works around it by driving the domain directly. That workaround is reasonable for domain rules,
/// but it cannot exercise an endpoint's permission guard, its HTTP contract, or its status codes.
/// For a ticket that was once reported as built on the strength of an enum, testing through the
/// real endpoint with a real permission is the difference between evidence and inference.
///
/// Staff users have SupplierId = null, which is what makes them "staff" to IScopeContext: the row
/// scoping treats a null SupplierId as unrestricted (see GetAuditLogHandler for the same rule).
/// </summary>
public static class StaffTestClient
{
    public const string Password = "StaffIntegration#2026!";

    public static async Task<HttpClient> CreateAsync(PostgresApiFixture fixture, string role) =>
        await CreateAsync(fixture, role, organizationId: null);

    /// <summary>EPIC-07: RFQ row-scoping keys on the caller's OrganizationId claim (IScopeContext.
    /// OrganizationId, sourced from AppUser.OrganizationId via LoginHandler ->
    /// jwtTokenService.IssueAccessToken). A plain CreateAsync staff user has OrganizationId null,
    /// which is correct for every non-RFQ staff test but means "no organization, no RFQ access" -
    /// same shape as scope.SupplierId is null meaning "no supplier, no access" on the supplier
    /// side. Callers testing RFQ endpoints must use this overload with a real Organization's Id.</summary>
    public static async Task<HttpClient> CreateAsync(PostgresApiFixture fixture, string role, Guid? organizationId)
    {
        var client = fixture.CreateClient();
        var email = $"staff-{Guid.NewGuid():N}@ministry.example";

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

            var user = new AppUser
            {
                Id = Guid.CreateVersion7(),
                UserName = email,
                Email = email,
                FullName = "Integration Staff",
                EmailConfirmed = true,
                IsActive = true,
                SupplierId = null,
                OrganizationId = organizationId,
            };

            var created = await userManager.CreateAsync(user, Password);
            if (!created.Succeeded)
            {
                throw new InvalidOperationException(
                    "Could not create the staff user: " +
                    string.Join(", ", created.Errors.Select(e => e.Description)));
            }

            await userManager.AddToRoleAsync(user, role);
        }

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        login.EnsureSuccessStatusCode();

        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());

        return client;
    }

    /// <summary>
    /// As <see cref="CreateAsync"/>, but for a role NFR-SEC-003 mandates MFA for (LoginHandler's
    /// <c>_mfaRequiredRoles</c> - system_admin by default). Plain <see cref="CreateAsync"/> gets a
    /// 403 <c>mfa_enrollment_required</c> for these roles, which is MSP-75's own discovery: nothing
    /// before this ticket had exercised a system_admin session through the real login endpoint.
    ///
    /// <para>Enrollment is done directly via <c>UserManager</c> rather than the HTTP enroll
    /// endpoint, because that endpoint requires an authenticated session and a not-yet-enrolled
    /// system_admin cannot obtain one - the same bootstrap gap exists in production; a real
    /// deployment seeds the first system_admin's enrollment out of band.</para>
    ///
    /// <para><b>The TOTP code is computed here, not via <c>UserManager.GenerateTwoFactorTokenAsync</c>.</b>
    /// Found empirically while building this: ASP.NET Core Identity's built-in "Authenticator"
    /// provider always returns null from <c>GenerateAsync</c> - by design, since a real authenticator
    /// app computes the code client-side from the shared secret, and the server only ever
    /// <i>verifies</i> a submitted code, never generates one to send. <see cref="ComputeTotp"/> is
    /// the standard RFC 6238 algorithm (HMAC-SHA1, 30s step, 6 digits) applied to the same Base32
    /// key <c>GetAuthenticatorKeyAsync</c> returns - the same computation any real authenticator app
    /// would perform, and the login round trip itself still goes through real HTTP, preserving the
    /// "evidence, not inference" reasoning above.</para>
    /// </summary>
    public static async Task<HttpClient> CreateWithMfaAsync(PostgresApiFixture fixture, string role)
    {
        var client = fixture.CreateClient();
        var email = $"staff-mfa-{Guid.NewGuid():N}@ministry.example";
        string key;

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

            var user = new AppUser
            {
                Id = Guid.CreateVersion7(),
                UserName = email,
                Email = email,
                FullName = "Integration Staff (MFA)",
                EmailConfirmed = true,
                IsActive = true,
                SupplierId = null,
            };

            var created = await userManager.CreateAsync(user, Password);
            if (!created.Succeeded)
            {
                throw new InvalidOperationException(
                    "Could not create the staff user: " +
                    string.Join(", ", created.Errors.Select(e => e.Description)));
            }

            await userManager.AddToRoleAsync(user, role);
            await userManager.ResetAuthenticatorKeyAsync(user);
            key = (await userManager.GetAuthenticatorKeyAsync(user))!;
            await userManager.SetTwoFactorEnabledAsync(user, true);
        }

        // First leg: password only. Must come back as the "code needed" challenge, not success or
        // enrollment-required - proves the enrollment above actually took effect.
        var firstLeg = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        if (firstLeg.StatusCode != HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException(
                $"Expected the password-only login leg to come back mfa_required (401); got {(int)firstLeg.StatusCode}.");
        }

        var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email, password = Password, totpCode = ComputeTotp(key) });
        login.EnsureSuccessStatusCode();

        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());

        return client;
    }

    private static string ComputeTotp(string base32Secret)
    {
        var key = Base32Decode(base32Secret);
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(counterBytes);

        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counterBytes);
        var offset = hash[^1] & 0x0F;
        var binaryCode = ((hash[offset] & 0x7F) << 24)
            | ((hash[offset + 1] & 0xFF) << 16)
            | ((hash[offset + 2] & 0xFF) << 8)
            | (hash[offset + 3] & 0xFF);
        return (binaryCode % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        input = input.TrimEnd('=').ToUpperInvariant();
        var bits = new StringBuilder();
        foreach (var c in input) bits.Append(Convert.ToString(alphabet.IndexOf(c), 2).PadLeft(5, '0'));
        var bytes = new List<byte>();
        for (var i = 0; i + 8 <= bits.Length; i += 8) bytes.Add(Convert.ToByte(bits.ToString(i, 8), 2));
        return [.. bytes];
    }
}
