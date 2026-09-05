using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// §12.1's response bodies, field by field - the last unswept section of §12.
///
/// <para>Two of its shapes are deliberately NOT conformed, and this suite asserts the divergence
/// rather than leaving it undocumented: the register response stays enumeration-safe (D-25) and the
/// login body carries no <c>user</c> object (D-26). A test that only checked the conformed fields
/// would let either of those be "fixed" later by someone reading §12.1 and not the reasoning.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AuthResponseContractTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task The_login_body_carries_the_documented_token_fields_and_no_user_object()
    {
        var (_, email) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, "Auth Contract Co");

        // A fresh login, so the body is read from the wire rather than out of a helper.
        var raw = fixture.CreateRawClient();
        var login = await raw.PostAsJsonAsync("/api/v1/auth/login",
            new { email, password = SupplierTestClient.Password });

        login.StatusCode.Should().Be(HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();

        // Conformed: §12.1 names both, and both are now emitted.
        body.GetProperty("tokenType").GetString().Should().Be("Bearer");
        body.GetProperty("expiresIn").GetInt32().Should().BeGreaterThan(0);

        // Kept alongside rather than replaced: an absolute expiry does not ask the client to trust
        // its own clock against the server's, and the SPA already reads it.
        body.GetProperty("accessTokenExpiresAt").GetDateTimeOffset()
            .Should().BeAfter(DateTimeOffset.UtcNow);
        body.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();

        // NOT conformed, deliberately (D-26). §12.1 shows a `user` object carrying roles and
        // permissions; the SPA reads those from the access token's own claims, so a second copy in
        // the body would be a second source of truth for authorization data - and the two disagree
        // the moment a role changes mid-session.
        body.TryGetProperty("user", out _).Should().BeFalse(
            "D-26: identity travels in the token's claims, not twice");
        var rawJson = body.ToString();
        foreach (var claimField in new[] { "permissions", "roles" })
        {
            rawJson.Should().NotContain(claimField,
                $"'{claimField}' belongs to the token, and duplicating it into the body creates two answers");
        }
    }

    [Fact]
    public async Task Registration_answers_the_same_way_whether_or_not_the_email_is_taken()
    {
        // D-25: §12.1 documents 201 + Location + onboardingState/email/emailVerified/createdAt, and a
        // 409 DUPLICATE_RESOURCE for a taken email. That 409 IS an account-enumeration oracle, and so
        // are the four extra fields. The code answers 200 with an identical body either way, and this
        // test is the reason it cannot be quietly "conformed" back.
        var payload = new
        {
            email = $"contract-{Guid.NewGuid():N}@example.sy",
            password = "Str0ng!Passw0rd!2026",
            displayNameAr = "شركة العقد", displayNameEn = $"Contract Co {Guid.NewGuid():N}"[..24],
            registrationNumber = $"RC-{Guid.NewGuid():N}"[..12],
            representativeName = "ليان الأحمد",
            representativePhone = "+963900000000",
        };

        var raw = fixture.CreateRawClient();
        var first = await raw.PostAsJsonAsync("/api/v1/auth/register", payload);
        var second = await raw.PostAsJsonAsync("/api/v1/auth/register", payload);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK,
            "D-25: a 409 here would tell an attacker the address is registered");

        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();

        // §12.1/R-9's spelling, which IS conformed - the field name was referenceCode.
        firstBody.GetProperty("supplierCode").GetString().Should().StartWith("SUP-");
        secondBody.GetProperty("supplierCode").ValueKind.Should().Be(JsonValueKind.Null);

        // The shapes are otherwise identical, and none of §12.1's four extra fields appears - each of
        // them would confirm the account exists.
        firstBody.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            secondBody.EnumerateObject().Select(p => p.Name),
            "the two responses must be indistinguishable in shape");
        foreach (var oracle in new[] { "onboardingState", "emailVerified", "createdAt" })
        {
            firstBody.TryGetProperty(oracle, out _).Should().BeFalse(
                $"D-25: '{oracle}' on this response would confirm the account exists");
        }
        first.Headers.Location.Should().BeNull("a Location header naming the supplier is the same oracle");
    }

    [Fact]
    public async Task An_invalid_verification_token_answers_422_with_the_documented_code()
    {
        var raw = fixture.CreateRawClient();

        // §12.1: "Expired/invalid token -> 422 (VERIFICATION_TOKEN_INVALID)". It answered 400 with a
        // different slug.
        var response = await raw.PostAsJsonAsync("/api/v1/auth/verify-email",
            new { token = "F3K9-2XQ7-NOT-A-REAL-TOKEN" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().Should().Be("VERIFICATION_TOKEN_INVALID");
    }

    [Fact]
    public async Task Logout_answers_204_as_documented()
    {
        // The control for the divergences above: §12.1 is not wrong about everything, and where the
        // code already matches it the sweep says so rather than only listing gaps.
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Logout Contract Co");

        var response = await client.PostAsync("/api/v1/auth/logout", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
