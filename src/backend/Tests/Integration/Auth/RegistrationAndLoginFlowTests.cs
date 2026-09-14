// End-to-end registration, verification and login against a real Postgres container: it proves the full stack
// - API to Identity to EF Core to Postgres - works together rather than that each layer compiles against
// mocks.
//
// Registration answers 200 OK rather than 201 since MSP-73, because the enumeration fix made success and
// duplicate responses identical in shape, and the code field is supplierCode per §12.1/R-9, matching §12.2
// everywhere else. Login before verification is rejected.
//
// The verification test issues the same opaque verification token RegisterSupplierHandler issues, using the
// real ISecurityTokenService against the real database with no mocking, because SECURITY-ARCHITECTURE.md §1.6
// says the link carries only this token and never the user id. After verification, login succeeds with
// row-scoped claims present, and the refresh cookie must be present and httpOnly, which is ASVS L2's
// token-handling requirement.

namespace MotsSupplierPortal.Tests.Integration.Auth;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Tests.Integration;

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

        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("supplierCode").GetString().Should().StartWith("SUP-");

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "IntegrationTest#2026!" });

        loginResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("code").GetString().Should().Be("EMAIL_NOT_VERIFIED");
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

        loginResponse.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        cookies!.Should().Contain(c => c.Contains("mots_refresh_token") && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
    }
}
