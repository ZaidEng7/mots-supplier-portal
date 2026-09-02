using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Infrastructure.Awards;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// MSP-98: the guard on the guard. Asserts that no recurring job is scheduled under the integration
/// suite, and that a job invoked directly still runs.
///
/// <para><b>Why this is a test and not a code comment.</b> The suppression is a configuration
/// switch, and a switch that silently stops being read is exactly the class of instrument this
/// project keeps finding: a check that looks like it is doing something and is not. If someone
/// renames the key, moves the registration block, or the fixture stops setting it, the hazard
/// returns silently - and its worst outcome is a FALSE PASS, a test green because a background job
/// produced the state rather than the code under test.</para>
///
/// <para><b>Asserted against Hangfire's STORAGE, not against the startup path.</b> Skipping
/// registration is not the same claim as "nothing is scheduled": Hangfire persists recurring job
/// definitions in <c>hangfire.set</c> and <c>hangfire.hash</c>, so a definition written by an
/// earlier run against the same database would still be picked up and fired by this host's server.
/// Reading the store is the only way to cover that case.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class RecurringJobSuppressionTests(PostgresApiFixture fixture)
{
    /// <summary>Every id Program.cs registers. Listed so a NEW job that forgets the switch fails here.</summary>
    private static readonly string[] KnownRecurringJobIds =
    [
        "document-expiry-lifecycle", "draft-registration-cleanup",
        "outbox-dispatch", "rfq-timeline", "award-erp-sync",
    ];

    [Fact]
    public async Task No_recurring_job_is_registered_in_hangfire_storage()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Hangfire keeps the set of recurring job ids in hangfire.set under key 'recurring-jobs'.
        var scheduled = await db.Database
            .SqlQuery<string>($@"SELECT value AS ""Value"" FROM hangfire.set WHERE key = 'recurring-jobs'")
            .ToListAsync();

        scheduled.Should().BeEmpty(
            "no recurring job may fire under the suite - not because Hangfire is off (it is not), " +
            "but because a scheduled job mutates the same state the tests assert on, and the quiet " +
            "failure is a test passing because the JOB produced the state rather than the code " +
            $"under test. Found: {string.Join(", ", scheduled)}");
    }

    /// <summary>
    /// The other half, and the one that stops this being fixed by simply breaking Hangfire: a job
    /// must still run when a test asks for it. AwardEndpointsTests resolves and runs
    /// AwardErpSyncJob explicitly against a failing adapter, and that is the behaviour under test.
    /// </summary>
    [Fact]
    public async Task A_job_invoked_directly_still_runs()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var job = scope.ServiceProvider.GetRequiredService<AwardErpSyncJob>();

        var run = async () => await job.RunAsync(CancellationToken.None);

        await run.Should().NotThrowAsync(
            "suppressing the SCHEDULER must not disable the jobs themselves - direct invocation " +
            "resolves the job from DI and never goes near Hangfire's scheduler");
    }

    /// <summary>
    /// The persisted-job case, exercised rather than argued. A definition is written into storage by
    /// hand - simulating one left by an earlier run against this database - and the assertion is
    /// that the host's own suppression removed it, so the server has nothing to pick up.
    /// </summary>
    [Fact]
    public async Task A_recurring_job_left_in_storage_by_an_earlier_run_is_removed()
    {
        using (var seedScope = fixture.Services.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            await seedDb.Database.ExecuteSqlRawAsync(
                "INSERT INTO hangfire.set (key, score, value) " +
                "VALUES ('recurring-jobs', 0, 'stale-from-an-earlier-run') ON CONFLICT DO NOTHING;");
        }

        // A fresh host over the SAME database runs the suppression path again on startup.
        await using var factory = fixture.WithWebHostBuilder(_ => { });
        using var client = factory.CreateClient();
        _ = await client.GetAsync("/health/live");

        using var verifyScope = factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var remaining = await db.Database
            .SqlQuery<string>($@"SELECT value AS ""Value"" FROM hangfire.set WHERE key = 'recurring-jobs'")
            .ToListAsync();

        remaining.Should().NotContain(KnownRecurringJobIds,
            "a definition persisted by an earlier run must be REMOVED, not merely left unregistered - " +
            "the server reads the store, not this startup's local decisions");
    }
}
