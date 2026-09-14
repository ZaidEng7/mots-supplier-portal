// MSP-73: Results.Conflict(new { error = "duplicate_email" }) against Results.Created let any caller learn
// whether an email is registered, unconditionally - confirmed live during PR #38, when RegistrationNumber
// uniqueness was added and mapped identically, deliberately matching the existing shape rather than inventing
// a second, differently-shaped leak. This fixes both at once: Success, DuplicateEmail and
// DuplicateRegistrationNumber all return the identical response now, in RegistrationEndpoints.cs, and the
// ALREADY-registered account is notified directly instead through
// EmailJobs.SendAlreadyRegisteredNoticeEmailAsync - so a legitimate user who forgot they had registered is
// helped, and nothing in the HTTP response tells a prober anything.
//
// The first two tests compare a genuine registration against each duplicate vector - the taken email, and a
// different email with the same registration number - and the body is a 202 SUCCESS body, which §7's error
// model does not touch, so the field is still `message`.
//
// The response body being identical does not close the enumeration vector on its own, which was measured
// directly against this endpoint before the timing test existed: a genuine registration averaged about 62ms,
// covering the transaction, Identity user creation and the audit log, while a duplicate short-circuit averaged
// about 5ms - a twelvefold gap a prober could use regardless of what the body says. RegisterSupplierHandler
// pads duplicate responses up toward a floor close to the genuine path's typical cost, MinResponseTime at
// 60ms, to close that. The test averages over several trials with FRESH targets each time: reusing one target
// repeatedly would trip NFR-SEC-009's per-target rate limit of 5 a minute partway through and read as near-0ms
// responses, which are a different and already-distinguishable signal - 429 rather than 200 - and not evidence
// the padding failed. The assertion sits at the 60ms floor minus a generous buffer for scheduler and CI
// jitter: loose enough not to be flaky, tight enough that the old ~5ms duplicate path would fail it by an
// order of magnitude rather than a hair.
//
// The notification tests assert the positive case against real Hangfire and the real Postgres job store - the
// same technique as EmailJobBehaviourTests' store-level check, used here to prove a job for the CORRECT
// existing user's id was actually enqueued rather than merely that no PII leaked into it. For a duplicate
// registration number, the primary user of the ORIGINAL supplier must be notified rather than anyone tied to
// the new attempt, of whom there is nobody, because no account was created for it.

namespace MotsSupplierPortal.Tests.Integration.Auth;

using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;

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
        firstBody.GetProperty("supplierCode").GetString().Should().NotBeNull("the genuine registration must have produced a real code");
        secondBody.GetProperty("supplierCode").ValueKind.Should().Be(JsonValueKind.Null,
            "no second Supplier was created for the duplicate, so there is nothing real to return - but the FIELD is still present (see PropertyNames assertion above)");
    }

    [Fact]
    public async Task A_genuine_registration_and_a_duplicate_registration_number_return_identically_shaped_responses()
    {
        var client = fixture.CreateClient();
        var registrationNumber = $"RC-{Guid.NewGuid():N}"[..12];

        var first = await client.PostAsJsonAsync("/api/v1/auth/register",
            RegistrationPayload($"itest-{Guid.NewGuid():N}@example.com", registrationNumber));
        var second = await client.PostAsJsonAsync("/api/v1/auth/register",
            RegistrationPayload($"itest-{Guid.NewGuid():N}@example.com", registrationNumber));

        first.StatusCode.Should().Be(second.StatusCode);

        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();

        PropertyNames(firstBody).Should().BeEquivalentTo(PropertyNames(secondBody),
            "email-duplicate and registration-number-duplicate must be exactly as indistinguishable from success as each other - one non-enumerating fix, not two differently-shaped ones");
        secondBody.GetProperty("supplierCode").ValueKind.Should().Be(JsonValueKind.Null);
    }

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
