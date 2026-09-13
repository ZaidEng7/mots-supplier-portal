// A registered, verified, signed-in supplier: the starting position for most integration tests.
//
// Extracted because each test class had grown its own near-identical copy of the register, verify, sign-in,
// attach-token sequence.
//
// Each call creates a distinct supplier, so tests sharing the collection's single database do not collide.
//
//
// THE VARIANTS, AND WHY EACH EXISTS
//
// One without the automatic version header. The suite's default client sends a current precondition on every
// mutation, which is what keeps three hundred tests written before that guard passing, and a test about the
// precondition itself needs a caller that sends exactly what the test says it sends and nothing else.
//
// One that also returns the generated address, so a test can sign in again and inspect the sign-in response
// itself: the refresh cookie is set on that response and is not observable from an already-authenticated client.
//
// And a SECOND user belonging to the same supplier, for tests proving a screen is scoped to the company rather
// than to the person reading it. That one is seeded through the user manager rather than the invitation flow,
// because the invitation is a separate feature with its own tests and driving it here would make a scoping test
// fail whenever invitations broke.
//
// The verification token is issued through the real token service against the real database. No mocking, and no
// scraping the link out of a log, which a privacy fix deliberately no longer prints.

namespace MotsSupplierPortal.Tests.Integration;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;

public static class SupplierTestClient
{
    public const string Password = "IntegrationTest#2026!";

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

    public static async Task<HttpClient> CreateColleagueAsync(PostgresApiFixture fixture, Guid supplierId)
    {
        var client = fixture.CreateClient();
        var email = $"colleague-{Guid.NewGuid():N}@supplier.example";

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

            var user = new AppUser
            {
                Id = Guid.CreateVersion7(),
                UserName = email,
                Email = email,
                FullName = "Integration Colleague",
                EmailConfirmed = true,
                IsActive = true,
                SupplierId = supplierId,
                OrganizationId = null,
            };

            var created = await userManager.CreateAsync(user, Password);
            if (!created.Succeeded)
            {
                throw new InvalidOperationException(
                    "Could not create the colleague: " + string.Join(", ", created.Errors.Select(e => e.Description)));
            }

            await userManager.AddToRoleAsync(user, Roles.SupplierUser);
        }

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        var body = await login.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());

        return client;
    }

    public static async Task<HttpClient> CreateVerifiedSupplierAsync(PostgresApiFixture fixture, string displayNameEn) =>
        (await CreateVerifiedSupplierWithEmailAsync(fixture, displayNameEn)).Client;

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
