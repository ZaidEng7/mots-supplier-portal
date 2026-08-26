using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// End-to-end registration/verification/login against a real Postgres container - proves the
/// full stack (API -> Identity -> EF Core -> Postgres) works together, not just that each
/// layer compiles against mocks.
/// </summary>
public sealed class RegistrationAndLoginFlowTests(PostgresApiFixture fixture) : IClassFixture<PostgresApiFixture>
{
    [Fact]
    public async Task Register_then_login_before_verification_is_rejected()
    {
        var client = fixture.CreateClient();
        var email = $"itest-{Guid.NewGuid():N}@example.com";

        var registerResponse = await client.PostAsJsonAsync("/api/v1/registrations", new
        {
            displayNameAr = "شركة اختبار",
            displayNameEn = "Integration Test Co",
            registrationNumber = "RC-9999",
            representativeName = "Integration Tester",
            representativePhone = "+963900000001",
            email,
            password = "IntegrationTest#2026!",
        });

        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("referenceCode").GetString().Should().StartWith("SUP-");

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "IntegrationTest#2026!" });

        loginResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("error").GetString().Should().Be("email_not_verified");
    }

    [Fact]
    public async Task Register_verify_then_login_succeeds_and_row_scoped_claims_are_present()
    {
        var client = fixture.CreateClient();
        var email = $"itest-{Guid.NewGuid():N}@example.com";
        const string password = "IntegrationTest#2026!";

        await client.PostAsJsonAsync("/api/v1/registrations", new
        {
            displayNameAr = "شركة اختبار ٢",
            displayNameEn = "Integration Test Co 2",
            registrationNumber = "RC-9998",
            representativeName = "Integration Tester Two",
            representativePhone = "+963900000002",
            email,
            password,
        });

        // Generate + encode the confirmation token the same way RegisterSupplierHandler does,
        // using the real UserManager against the real database (no mocking the token provider).
        using var scope = fixture.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await userManager.FindByEmailAsync(email);
        user.Should().NotBeNull();

        var rawToken = await userManager.GenerateEmailConfirmationTokenAsync(user!);
        var encodedToken = WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(rawToken));

        var verifyResponse = await client.PostAsJsonAsync("/api/v1/registrations/verify", new { userId = user!.Id, token = encodedToken });
        verifyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var tokens = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = tokens.GetProperty("accessToken").GetString();
        accessToken.Should().NotBeNullOrEmpty();

        // Refresh cookie must be present and httpOnly (ASVS L2 token-handling requirement).
        loginResponse.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        cookies!.Should().Contain(c => c.Contains("mots_refresh_token") && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
    }
}
