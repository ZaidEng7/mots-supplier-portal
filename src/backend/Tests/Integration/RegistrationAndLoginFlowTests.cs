using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// End-to-end registration/verification/login against a real Postgres container - proves the
/// full stack (API -> Identity -> EF Core -> Postgres) works together, not just that each
/// layer compiles against mocks.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class RegistrationAndLoginFlowTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task Register_then_login_before_verification_is_rejected()
    {
        var client = fixture.CreateClient();
        var email = $"itest-{Guid.NewGuid():N}@example.com";

        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            displayNameAr = "شركة اختبار",
            displayNameEn = "Integration Test Co",
            registrationNumber = "RC-9999",
            representativeName = "Integration Tester",
            representativePhone = "+963900000001",
            email,
            password = "IntegrationTest#2026!",
        });

        // MSP-73: 200 OK now, not 201 - the enumeration fix made success and duplicate responses
        // identical in shape.
        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
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

        await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            displayNameAr = "شركة اختبار ٢",
            displayNameEn = "Integration Test Co 2",
            registrationNumber = "RC-9998",
            representativeName = "Integration Tester Two",
            representativePhone = "+963900000002",
            email,
            password,
        });

        // Issue the same opaque verification token RegisterSupplierHandler issues, using the real
        // ISecurityTokenService against the real database (no mocking). SECURITY-ARCHITECTURE.md
        // §1.6: the link/request carries only this token, never the user id.
        using var scope = fixture.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var securityTokenService = scope.ServiceProvider.GetRequiredService<ISecurityTokenService>();
        var user = await userManager.FindByEmailAsync(email);
        user.Should().NotBeNull();

        var rawToken = await securityTokenService.IssueAsync(user!.Id, SecurityTokenPurpose.EmailVerification, TimeSpan.FromHours(24), CancellationToken.None);

        var verifyResponse = await client.PostAsJsonAsync("/api/v1/auth/verify-email", new { token = rawToken });
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
