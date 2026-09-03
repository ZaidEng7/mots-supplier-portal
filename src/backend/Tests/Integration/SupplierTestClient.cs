using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// Shared setup for "a registered, email-verified, logged-in supplier" - the starting position for
/// most integration tests. Extracted because each test class had grown its own near-identical copy
/// of the register -> issue token -> verify -> login -> attach bearer sequence.
/// </summary>
public static class SupplierTestClient
{
    public const string Password = "IntegrationTest#2026!";

    /// <summary>Returns a client whose Authorization header carries a live access token for a
    /// freshly registered supplier. Each call creates a distinct supplier, so tests sharing the
    /// collection's single database do not collide.</summary>
    /// <summary>
    /// The same authenticated caller, but on a client that does NOT attach ETags automatically.
    ///
    /// <para>The suite's default client sends a current If-Match on every mutation, which is what
    /// keeps three hundred tests written before §8.1 passing. A test about the precondition itself
    /// needs a caller that sends exactly what the test says it sends and nothing else.</para>
    /// </summary>
    public static Task<HttpClient> CloneWithoutETagsAsync(PostgresApiFixture fixture, HttpClient authenticated)
    {
        var raw = fixture.CreateRawClient();
        raw.DefaultRequestHeaders.Authorization = authenticated.DefaultRequestHeaders.Authorization;
        foreach (var header in authenticated.DefaultRequestHeaders.Where(h => h.Key != "Authorization"))
        {
            raw.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
        }
        return Task.FromResult(raw);
    }

    public static async Task<HttpClient> CreateVerifiedSupplierAsync(PostgresApiFixture fixture, string displayNameEn) =>
        (await CreateVerifiedSupplierWithEmailAsync(fixture, displayNameEn)).Client;

    /// <summary>As <see cref="CreateVerifiedSupplierAsync"/>, but also returns the generated email so
    /// a test can log in again and inspect the login response itself - the refresh cookie is set on
    /// that response, and is not observable from an already-authenticated client.</summary>
    public static async Task<(HttpClient Client, string Email)> CreateVerifiedSupplierWithEmailAsync(
        PostgresApiFixture fixture, string displayNameEn)
    {
        var client = fixture.CreateClient();
        var email = $"itest-{Guid.NewGuid():N}@example.com";

        await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            displayNameAr = "شركة اختبار",
            displayNameEn,
            registrationNumber = $"RC-{Guid.NewGuid():N}"[..12],
            representativeName = "Integration Tester",
            representativePhone = "+963900000000",
            email,
            password = Password,
        });

        await VerifyEmailAsync(fixture, client, email);

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());

        return (client, email);
    }

    /// <summary>Issues the same opaque verification token RegisterSupplierHandler issues, using the
    /// real ISecurityTokenService against the real database - no mocking, and no scraping the link
    /// out of a log (which the MSP-61 fix deliberately no longer prints).</summary>
    private static async Task VerifyEmailAsync(PostgresApiFixture fixture, HttpClient client, string email)
    {
        using var scope = fixture.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var securityTokenService = scope.ServiceProvider.GetRequiredService<ISecurityTokenService>();

        var user = await userManager.FindByEmailAsync(email);
        var rawToken = await securityTokenService.IssueAsync(
            user!.Id, SecurityTokenPurpose.EmailVerification, TimeSpan.FromHours(24), CancellationToken.None);

        await client.PostAsJsonAsync("/api/v1/auth/verify-email", new { token = rawToken });
    }
}
