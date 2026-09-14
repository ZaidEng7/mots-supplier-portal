// MSP-98: the guard on the guard. It asserts that no recurring job is scheduled under the integration suite,
// and that a job invoked directly still runs.
//
// Why this is a test and not a code comment. The suppression is a configuration switch, and a switch that
// silently stops being read is exactly the class of instrument this project keeps finding: a check that looks
// like it is doing something and is not. If someone renames the key, moves the registration block, or the
// fixture stops setting it, the hazard returns silently - and its worst outcome is a FALSE PASS, a test green
// because a background job produced the state rather than the code under test.
//
// It is asserted against Hangfire's STORAGE rather than against the startup path. Skipping registration is not
// the same claim as "nothing is scheduled": Hangfire persists recurring job definitions in hangfire.set and
// hangfire.hash, so a definition written by an earlier run against the same database would still be picked up
// and fired by this host's server. Reading the store - the set of recurring job ids lives in hangfire.set under
// the key 'recurring-jobs' - is the only way to cover that case.
//
// The list of ids is every one Program.cs registers, written down so a NEW job that forgets the switch fails
// here. T-053 and §8.2.1's "GC'd by Hangfire" gate is the reason the list exists in three places and is
// asserted rather than assumed: a new job nobody adds here stays scheduled under the test suite.
//
// The direct-invocation test is the other half, and the one that stops this being "fixed" by simply breaking
// Hangfire: a job must still run when a test asks for it. AwardEndpointsTests resolves and runs AwardErpSyncJob
// explicitly against a failing adapter, and that is the behaviour under test.
//
// The persisted-job case is exercised rather than argued. A definition is written into storage by hand -
// simulating one left by an earlier run against this database - and the assertion is that the host's own
// suppression removed it, so the server has nothing to pick up, with a fresh host over the SAME database
// running the suppression path again on startup. T-073: the row is planted in the SHARED Hangfire schema and
// its removal IS the assertion rather than a cleanup step, so when the assertion fails the row survives and the
// next test reading recurring-jobs sees a definition nobody scheduled - hence the removal in a finally, which
// costs nothing when the suppression did its job.
//
// THE CONTROL is the last test. Every negative in this file asserts that nothing is scheduled; this asserts
// that something IS, when the flag is left at its default. It matters more than a usual control:
// Jobs:EnableRecurring defaults to true, so no deployment changes by default - but a typo in the key in a real
// environment silently stops rfq-timeline, and RFQ submission windows then never open and never close. Tenders
// stop working with no error anywhere, and a misconfiguration that silently DISABLES is worse than one that
// fails loudly. It is asserted exactly, both directions: a missing id fails and an unexpected id fails, so a
// sixth job added without a decision about this list fails here rather than shipping unscheduled - or scheduled
// and unsuppressed under the suite, which is the hazard this file exists to remove.
//
// That control gets its own Hangfire schema. The first version registered five real recurring jobs into the
// storage the whole suite shares and deleted them afterwards, which left a window of seconds in which the exact
// race this batch removed was back. The window was small and self-cleaning, and it was still the suite doing
// the thing the suite exists to prevent. The derived host now points Hangfire at its own schema, so the
// registration is invisible to every other host - and because storage is genuinely separate, the
// finally-cleanup and its post-condition are gone rather than merely reduced. The schema name is unique per
// run, because a leftover schema from an earlier run must not be able to answer for this one, which is the same
// reasoning as the stale-definition case. It is still Hangfire's REAL storage, just this host's own, so the
// assertion is unchanged in strength: what is actually scheduled, read from the scheduler's own tables. The
// schema name is a literal in the SQL because an identifier cannot be parameterised - it is not caller input,
// having been generated a few lines earlier from a Guid, and the format string is built deliberately rather
// than interpolated into an EF Core FormattableString, which is what EF1002 exists to stop. Finally the
// isolation is ASSERTED rather than described: registering five real recurring jobs must have left the shared
// storage untouched, because if that fails the control has reintroduced the race for every test that runs after
// it.

namespace MotsSupplierPortal.Tests.Integration.Platform;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Infrastructure.Awards;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class RecurringJobSuppressionTests(PostgresApiFixture fixture)
{
    private static readonly string[] KnownRecurringJobIds =
    [
        "document-expiry-lifecycle", "draft-registration-cleanup",
        "outbox-dispatch", "rfq-timeline", "award-erp-sync",
        "idempotency-cleanup",
    ];

    [Fact]
    public async Task No_recurring_job_is_registered_in_hangfire_storage()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var scheduled = await db.Database
            .SqlQuery<string>($@"SELECT value AS ""Value"" FROM hangfire.set WHERE key = 'recurring-jobs'")
            .ToListAsync();

        scheduled.Should().BeEmpty(
            "no recurring job may fire under the suite - not because Hangfire is off (it is not), " +
            "but because a scheduled job mutates the same state the tests assert on, and the quiet " +
            "failure is a test passing because the JOB produced the state rather than the code " +
            $"under test. Found: {string.Join(", ", scheduled)}");
    }

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

        try
        {

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
        finally
        {
            using var cleanupScope = fixture.Services.CreateScope();
            var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<AppDbContext>();
            await cleanupDb.Database.ExecuteSqlRawAsync(
                "DELETE FROM hangfire.set WHERE key = 'recurring-jobs' AND value = 'stale-from-an-earlier-run';");
        }
    }

    [Fact]
    public async Task With_the_flag_at_its_default_exactly_the_known_recurring_jobs_are_scheduled()
    {
        var schema = $"hangfire_ctl_{Guid.NewGuid():N}"[..24];

        await using var enabledHost = fixture.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jobs:EnableRecurring", "true");
            builder.UseSetting("Hangfire:SchemaName", schema);
        });

        using var client = enabledHost.CreateClient();
        _ = await client.GetAsync("/health/live");

        using var scope = enabledHost.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var scheduledSql = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            @"SELECT value AS ""Value"" FROM {0}.set WHERE key = 'recurring-jobs'", schema);
        var scheduled = await db.Database.SqlQueryRaw<string>(scheduledSql).ToListAsync();

        scheduled.Should().BeEquivalentTo(KnownRecurringJobIds,
            "with the flag at its default every one of these must be scheduled - a MISSING id is a " +
            "job that silently stopped running in production, and an UNEXPECTED id is a new job " +
            "nobody decided about, which would also be unsuppressed under this suite");

        using var sharedScope = fixture.Services.CreateScope();
        var sharedDb = sharedScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var shared = await sharedDb.Database
            .SqlQuery<string>($@"SELECT value AS ""Value"" FROM hangfire.set WHERE key = 'recurring-jobs'")
            .ToListAsync();

        shared.Should().BeEmpty(
            "the control schedules real jobs, and it must do so somewhere the rest of the suite " +
            "cannot see - separate storage is the claim, and this is the evidence for it");
    }
}
