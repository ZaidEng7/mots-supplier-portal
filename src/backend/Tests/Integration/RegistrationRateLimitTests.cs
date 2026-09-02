using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// NFR-SEC-009: no bot/abuse protection existed on POST /api/v1/auth/register before this - only
/// the shared "auth-strict" per-IP policy (10/min) also applied to login/forgot-password. This
/// gives registration its own, tighter per-IP policy ("register-strict", default 5/min) plus a
/// tighter per-target (per-email) budget on PerTargetRateLimiter's "register" surface (also 5/min,
/// down from the previous shared 10/min) - both dimensions tested here, per "prove the Nth+1
/// attempt is rejected, and a normal single registration is not blocked".
///
/// CAPTCHA was considered and explicitly not built: no CAPTCHA provider (hCaptcha/Turnstile/etc)
/// is wired into this codebase on either side, and provisioning one needs a real external account
/// and site/secret key pair - not something obtainable inside this change. Strengthening the
/// existing, already-real, already-tested rate-limiting mechanism was the lighter option that
/// covers most of the risk without a new external dependency.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class RegistrationRateLimitTests(PostgresApiFixture fixture)
{
    private static object RegistrationPayload(string email, string? registrationNumber = null) => new
    {
        displayNameAr = "شركة اختبار",
        displayNameEn = $"Rate Limit Test {Guid.NewGuid():N}"[..30],
        registrationNumber,
        representativeName = "Rate Limit Tester",
        representativePhone = "+963900000000",
        email,
        password = "RateLimitTest#2026!",
    };

    [Fact]
    public async Task A_single_legitimate_registration_is_not_rate_limited()
    {
        // Default fixture settings (RateLimiting:RegisterPermitLimit cranked up for the shared
        // host) - proves the mechanism does not interfere with the ordinary case.
        var client = fixture.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register",
            RegistrationPayload($"itest-{Guid.NewGuid():N}@example.com"));

        response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Per_ip_the_Nth_plus_one_registration_attempt_in_one_minute_is_rejected()
    {
        // A dedicated host with RegisterPermitLimit lowered to a small, deterministic number -
        // the shared fixture cranks this up specifically so other tests aren't throttled, so
        // proving the limit exists at all needs its own host with a real, low ceiling.
        const int permitLimit = 3;
        await using var limitedFactory = fixture.WithWebHostBuilder(builder =>
            builder.UseSetting("RateLimiting:RegisterPermitLimit", permitLimit.ToString()));
        using var client = limitedFactory.CreateClient();

        // Different email each call - this is the per-IP dimension specifically: one source
        // hitting many different targets rapidly, the shape an automated registration script
        // actually takes, not one person retrying against their own email.
        for (var i = 0; i < permitLimit; i++)
        {
            var response = await client.PostAsJsonAsync("/api/v1/auth/register",
                RegistrationPayload($"itest-{Guid.NewGuid():N}@example.com"));
            response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
                $"attempt {i + 1} of {permitLimit} is within budget and must not be rejected");
        }

        var overLimit = await client.PostAsJsonAsync("/api/v1/auth/register",
            RegistrationPayload($"itest-{Guid.NewGuid():N}@example.com"));
        overLimit.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            $"the {permitLimit + 1}th attempt from the same IP within the window must be rejected");
    }

    [Fact]
    public async Task Per_target_the_Nth_plus_one_attempt_against_the_same_email_in_one_minute_is_rejected()
    {
        // PerTargetRateLimiter's "register" surface limit (5/min) is a hardcoded constant, not
        // configuration-driven, so this runs against the shared fixture directly - its cranked-up
        // per-IP limit does not interfere with this, the per-TARGET dimension.
        var client = fixture.CreateClient();
        var email = $"itest-{Guid.NewGuid():N}@example.com";
        const int permitLimit = 5;

        for (var i = 0; i < permitLimit; i++)
        {
            var response = await client.PostAsJsonAsync("/api/v1/auth/register", RegistrationPayload(email));
            // The first call succeeds; calls 2..5 against the same email correctly fail as
            // duplicates (409 today) - what matters here is that none of the first 5 are
            // rate-limited (429), proving the per-target budget itself is 5, not fewer.
            response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
                $"attempt {i + 1} of {permitLimit} against the same email is within budget");
        }

        var overLimit = await client.PostAsJsonAsync("/api/v1/auth/register", RegistrationPayload(email));
        overLimit.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            $"the {permitLimit + 1}th attempt against the same email within the window must be rejected");
    }
}
