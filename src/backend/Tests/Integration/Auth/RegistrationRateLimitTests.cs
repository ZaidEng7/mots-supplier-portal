// NFR-SEC-009: no bot or abuse protection existed on POST /api/v1/auth/register before this - only the shared
// "auth-strict" per-IP policy of 10 a minute, which also applied to login and forgot-password. This gives
// registration its own tighter per-IP policy, "register-strict" at a default of 5 a minute, plus a tighter
// per-target (per-email) budget on PerTargetRateLimiter's "register" surface, also 5 a minute, down from the
// previous shared 10. Both dimensions are tested here, per "prove the Nth+1 attempt is rejected, and a normal
// single registration is not blocked".
//
// CAPTCHA was considered and explicitly not built: no CAPTCHA provider - hCaptcha, Turnstile or another - is
// wired into this codebase on either side, and provisioning one needs a real external account and a site and
// secret key pair, which is not something obtainable inside this change. Strengthening the existing,
// already-real, already-tested rate-limiting mechanism was the lighter option that covers most of the risk
// without a new external dependency.
//
// The legitimate single registration runs on the default fixture settings, where
// RateLimiting:RegisterPermitLimit is cranked up for the shared host, which proves the mechanism does not
// interfere with the ordinary case.
//
// The per-IP test uses a dedicated host with RegisterPermitLimit lowered to a small, deterministic number: the
// shared fixture cranks it up specifically so other tests are not throttled, so proving the limit exists at
// all needs its own host with a real, low ceiling. Each call uses a different email, because this is the per-IP
// dimension specifically - one source hitting many different targets rapidly, which is the shape an automated
// registration script actually takes, rather than one person retrying against their own email.
//
// The per-target test runs against the shared fixture directly, because PerTargetRateLimiter's "register"
// surface limit of 5 a minute is a hardcoded constant rather than configuration-driven, and the fixture's
// cranked-up per-IP limit does not interfere with the per-TARGET dimension. The first call succeeds and calls
// two through five against the same email correctly fail as duplicates - 409 today - and what matters is that
// none of the first five are rate-limited with a 429, which proves the per-target budget itself is 5 rather
// than fewer.

namespace MotsSupplierPortal.Tests.Integration.Auth;

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Tests.Integration;

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
        var client = fixture.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register",
            RegistrationPayload($"itest-{Guid.NewGuid():N}@example.com"));

        response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Per_ip_the_Nth_plus_one_registration_attempt_in_one_minute_is_rejected()
    {
        const int permitLimit = 3;
        await using var limitedFactory = fixture.WithWebHostBuilder(builder =>
            builder.UseSetting("RateLimiting:RegisterPermitLimit", permitLimit.ToString()));
        using var client = limitedFactory.CreateClient();

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
        var client = fixture.CreateClient();
        var email = $"itest-{Guid.NewGuid():N}@example.com";
        const int permitLimit = 5;

        for (var i = 0; i < permitLimit; i++)
        {
            var response = await client.PostAsJsonAsync("/api/v1/auth/register", RegistrationPayload(email));
            response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
                $"attempt {i + 1} of {permitLimit} against the same email is within budget");
        }

        var overLimit = await client.PostAsJsonAsync("/api/v1/auth/register", RegistrationPayload(email));
        overLimit.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            $"the {permitLimit + 1}th attempt against the same email within the window must be rejected");
    }
}
