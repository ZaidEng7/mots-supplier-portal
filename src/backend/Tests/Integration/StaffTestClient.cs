// A signed-in member of staff, for endpoints guarded by a staff permission.
//
// This did not exist for a long time, and one suite records the gap explicitly and works around it by driving the
// domain directly. That workaround is reasonable for domain rules, but it cannot exercise an endpoint's
// permission guard, its contract, or its status codes.
//
// For work that was once reported as built on the strength of an enumeration, testing through the real endpoint
// with a real permission is the difference between evidence and inference.
//
// Staff accounts carry no company, which is what makes them staff to the scope: row scoping treats an absent
// company as unrestricted.
//
//
// THE ORGANIZATION OVERLOAD IS NOT OPTIONAL FOR TENDER TESTS
//
// Tender row-scoping keys on the caller's organization claim, which comes from the account and is issued in the
// token.
//
// A plain staff account has none, which is correct for every non-tender staff test but means no organization, no
// tender access, in the same shape that no company means no access on the supplier side. Callers testing tender
// endpoints must pass a real organization.
//
// Two further overloads exist because some callers need the account's own identifier: assigning an evaluator, and
// the lockout guards that are ABOUT the caller's own account.
//
//
// THE SECOND-FACTOR PATH, AND WHY THE CODE IS COMPUTED HERE
//
// A role the rules mandate a second factor for cannot sign in through the plain path at all; it is refused with
// an enrolment requirement. Nothing had exercised such a session through the real sign-in endpoint before.
//
// Enrolment is done through the user manager rather than the enrolment endpoint, because that endpoint requires an
// authenticated session and a not-yet-enrolled administrator cannot obtain one. The same bootstrap gap exists in
// production, where a real deployment seeds the first administrator's enrolment out of band.
//
// The code itself is computed here rather than asked of the framework. Found empirically: the framework's
// authenticator provider always returns nothing from its generate call, by design, because a real authenticator
// app computes the code from the shared secret and the server only ever verifies one.
//
// So this applies the standard algorithm to the same key the framework hands out, which is the same computation
// any authenticator app performs, and the sign-in round trip still goes through real HTTP, which preserves the
// evidence-not-inference reasoning above.
//
// The first leg is asserted as the code-needed challenge rather than as success or as an enrolment requirement,
// which proves the enrolment actually took effect.

namespace MotsSupplierPortal.Tests.Integration;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

public static class StaffTestClient
{
    public const string Password = "StaffIntegration#2026!";

    public static async Task<HttpClient> CreateAsync(PostgresApiFixture fixture, string role) =>
        await CreateAsync(fixture, role, organizationId: null);

    public static async Task<HttpClient> CreateAsync(PostgresApiFixture fixture, string role, Guid? organizationId) =>
        (await CreateWithEmailAsync(fixture, role, organizationId)).Client;

    public static async Task<(HttpClient Client, string Email)> CreateWithEmailAsync(
        PostgresApiFixture fixture, string role, Guid? organizationId)
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

        return (client, email);
    }

    public static async Task<(HttpClient Client, Guid UserId)> CreateWithIdAsync(PostgresApiFixture fixture, string role, Guid? organizationId = null)
    {
        var client = fixture.CreateClient();
        var email = $"staff-{Guid.NewGuid():N}@ministry.example";
        var userId = Guid.CreateVersion7();

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

            var user = new AppUser
            {
                Id = userId,
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

        return (client, userId);
    }

    public static async Task<HttpClient> CreateWithMfaAsync(PostgresApiFixture fixture, string role) =>
        (await CreateWithMfaAndIdAsync(fixture, role)).Client;

    public static async Task<(HttpClient Client, Guid UserId)> CreateWithMfaAndIdAsync(PostgresApiFixture fixture, string role)
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

        Guid userId;
        await using (var idScope = fixture.Services.CreateAsyncScope())
        {
            var db = idScope.ServiceProvider.GetRequiredService<AppDbContext>();
            userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).FirstAsync();
        }

        return (client, userId);
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
