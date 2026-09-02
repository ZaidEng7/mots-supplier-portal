using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// MSP-73: GET Results.Conflict(new { error = "duplicate_email" }) against Results.Created let any
/// caller learn whether an email is registered, unconditionally - confirmed live during PR #38 when
/// RegistrationNumber uniqueness was added and mapped identically, deliberately matching the
/// existing shape rather than inventing a second, differently-shaped leak. This fixes both at once:
/// Success, DuplicateEmail, and DuplicateRegistrationNumber all return the identical response now
/// (RegistrationEndpoints.cs), and the ALREADY-registered account is notified directly instead
/// (EmailJobs.SendAlreadyRegisteredNoticeEmailAsync) - a legitimate user who forgot they'd
/// registered is helped, and nothing in the HTTP response tells a prober anything.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class RegistrationEnumerationTests(PostgresApiFixture fixture)
{
    private static object RegistrationPayload(string email, string? registrationNumber = null) => new
    {
        displayNameAr = "شركة اختبار",
        displayNameEn = $"Enum Test {Guid.NewGuid():N}"[..30],
        registrationNumber,
        representativeName = "Enumeration Tester",
        representativePhone = "+963900000000",
        email,
        password = "EnumerationTest#2026!",
    };

    private static ISet<string> PropertyNames(JsonElement body) =>
        body.EnumerateObject().Select(p => p.Name).ToHashSet();

    [Fact]
    public async Task A_genuine_registration_and_a_duplicate_email_return_identically_shaped_responses()
    {
        var client = fixture.CreateClient();
        var email = $"itest-{Guid.NewGuid():N}@example.com";

        var first = await client.PostAsJsonAsync("/api/v1/auth/register", RegistrationPayload(email));
        var second = await client.PostAsJsonAsync("/api/v1/auth/register", RegistrationPayload(email));

        first.StatusCode.Should().Be(second.StatusCode, "a caller must not be able to distinguish new from duplicate by status code");

        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();

        PropertyNames(firstBody).Should().BeEquivalentTo(PropertyNames(secondBody),
            "the same set of JSON fields must be present either way - an extra field on one side (e.g. an error code) would itself be the leak");

        firstBody.GetProperty("message").GetString().Should().Be(secondBody.GetProperty("message").GetString());
        firstBody.GetProperty("referenceCode").GetString().Should().NotBeNull("the genuine registration must have produced a real code");
        secondBody.GetProperty("referenceCode").ValueKind.Should().Be(JsonValueKind.Null,
            "no second Supplier was created for the duplicate, so there is nothing real to return - but the FIELD is still present (see PropertyNames assertion above)");
    }

    [Fact]
    public async Task A_genuine_registration_and_a_duplicate_registration_number_return_identically_shaped_responses()
    {
        var client = fixture.CreateClient();
        var registrationNumber = $"RC-{Guid.NewGuid():N}"[..12];

        var first = await client.PostAsJsonAsync("/api/v1/auth/register",
            RegistrationPayload($"itest-{Guid.NewGuid():N}@example.com", registrationNumber));
        // Different email, same registration number - the OTHER duplicate vector, not email.
        var second = await client.PostAsJsonAsync("/api/v1/auth/register",
            RegistrationPayload($"itest-{Guid.NewGuid():N}@example.com", registrationNumber));

        first.StatusCode.Should().Be(second.StatusCode);

        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();

        PropertyNames(firstBody).Should().BeEquivalentTo(PropertyNames(secondBody),
            "email-duplicate and registration-number-duplicate must be exactly as indistinguishable from success as each other - one non-enumerating fix, not two differently-shaped ones");
        secondBody.GetProperty("referenceCode").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// The response body being identical doesn't close the enumeration vector on its own - measured
    /// directly against this endpoint before this test existed: a genuine registration averaged
    /// ~62ms (transaction, Identity user creation, audit log), a duplicate short-circuit averaged
    /// ~5ms, a 12x gap a prober could use regardless of what the body says. RegisterSupplierHandler
    /// pads duplicate responses up toward a floor close to the genuine path's typical cost
    /// (MinResponseTime, 60ms) to close that. Averaged over several trials with FRESH targets each
    /// time - reusing one target repeatedly would trip NFR-SEC-009's per-target rate limit (5/min)
    /// partway through and read as near-0ms responses that are a different, already-distinguishable
    /// signal (429, not 200), not evidence the padding failed.
    /// </summary>
    [Fact]
    public async Task Duplicate_and_genuine_registration_responses_take_comparable_time()
    {
        var client = fixture.CreateClient();
        const int trials = 6;
        var duplicateTimes = new List<long>();

        for (var i = 0; i < trials; i++)
        {
            var email = $"itest-{Guid.NewGuid():N}@example.com";
            await client.PostAsJsonAsync("/api/v1/auth/register", RegistrationPayload(email));

            var stopwatch = Stopwatch.StartNew();
            await client.PostAsJsonAsync("/api/v1/auth/register", RegistrationPayload(email));
            stopwatch.Stop();
            duplicateTimes.Add(stopwatch.ElapsedMilliseconds);
        }

        // 60ms floor minus generous buffer for scheduler/CI jitter - loose enough to not be flaky,
        // tight enough that the old ~5ms duplicate-path behavior would fail it by an order of
        // magnitude, not a hair.
        duplicateTimes.Average().Should().BeGreaterThan(35,
            $"the padding floor should keep duplicate responses close to genuine ones, not answer in ~5ms; observed: {string.Join(",", duplicateTimes)}");
    }

    [Fact]
    public async Task A_duplicate_email_registration_notifies_the_existing_account_not_the_submitter()
    {
        var client = fixture.CreateClient();
        var (_, existingEmail) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, "Existing Account Co");

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var existingUserId = await db.Users.Where(u => u.Email == existingEmail).Select(u => u.Id).SingleAsync();

            var response = await client.PostAsJsonAsync("/api/v1/auth/register", RegistrationPayload(existingEmail));
            response.EnsureSuccessStatusCode();

            // Real Hangfire, real Postgres job store - same technique as EmailJobBehaviourTests'
            // store-level check, here asserting the POSITIVE case: a job for the CORRECT existing
            // user's id was actually enqueued, not merely that no PII leaked into it.
            var jobArgsContainingUserId = await db.Database
                .SqlQuery<string>($@"SELECT arguments::text AS ""Value"" FROM hangfire.job
                                     WHERE invocationdata::text LIKE '%SendAlreadyRegisteredNoticeEmailAsync%'
                                     AND arguments::text LIKE {'%' + existingUserId.ToString() + '%'}")
                .ToListAsync();

            jobArgsContainingUserId.Should().NotBeEmpty(
                "the existing account's own user id must appear in an enqueued SendAlreadyRegisteredNoticeEmailAsync job");
        }
    }

    [Fact]
    public async Task A_duplicate_registration_number_notifies_the_existing_supplier_s_primary_user()
    {
        var client = fixture.CreateClient();
        var registrationNumber = $"RC-{Guid.NewGuid():N}"[..12];
        var originalEmail = $"itest-{Guid.NewGuid():N}@example.com";

        var original = await client.PostAsJsonAsync("/api/v1/auth/register", RegistrationPayload(originalEmail, registrationNumber));
        original.EnsureSuccessStatusCode();

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var originalUserId = await db.Users.Where(u => u.Email == originalEmail).Select(u => u.Id).SingleAsync();

        // A different email attempting to register the SAME registration number - the primary
        // user of the ORIGINAL supplier must be notified, not anyone tied to this new attempt
        // (there is no one - no account was created for it).
        var duplicate = await client.PostAsJsonAsync("/api/v1/auth/register",
            RegistrationPayload($"itest-{Guid.NewGuid():N}@example.com", registrationNumber));
        duplicate.EnsureSuccessStatusCode();

        var jobArgsContainingUserId = await db.Database
            .SqlQuery<string>($@"SELECT arguments::text AS ""Value"" FROM hangfire.job
                                 WHERE invocationdata::text LIKE '%SendAlreadyRegisteredNoticeEmailAsync%'
                                 AND arguments::text LIKE {'%' + originalUserId.ToString() + '%'}")
            .ToListAsync();

        jobArgsContainingUserId.Should().NotBeEmpty(
            "the original supplier's primary user id must appear in an enqueued notice job");
    }
}
