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
    public static async Task<HttpClient> CreateVerifiedSupplierAsync(PostgresApiFixture fixture, string displayNameEn)
    {
        var client = fixture.CreateClient();
        var email = $"itest-{Guid.NewGuid():N}@example.com";

        await client.PostAsJsonAsync("/api/v1/registrations", new
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

        return client;
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

        await client.PostAsJsonAsync("/api/v1/registrations/verify", new { token = rawToken });
    }
}
