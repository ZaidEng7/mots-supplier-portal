// The scheduled work: what runs on a timer, how often, and the switch that turns it all off.
//
//
// WHY THERE IS A SWITCH
//
// Scheduled work is registered only when it is on, and the integration test host turns it off. The job
// library itself is untouched: its server still runs, queued work like emails still processes, and a
// test that asks for a job explicitly still gets it, because direct invocation resolves the job from
// the container and never goes near the scheduler.
//
// Every job below changes state that tests also assert on, on a cadence measured in minutes, against
// the database the whole suite shares. That produced one loud failure, where the finance-sync job
// synced an award a test had set up to fail because the suite grew past a five-minute boundary, and
// the loud one is the lucky case.
//
// The mirror image is a test asserting a state a job also produces, such as a tender reaching its
// closed state or a document reaching expiry, and passing because the job did the work rather than the
// code under test. That test goes green and stays green.
//
// It is a configuration switch rather than a compiled-in condition, because the test host already
// configures itself that way and a compile-time flag would put the test host's behaviour somewhere the
// test host cannot see it.
//
//
// WHY THE IDENTIFIERS COME FROM ONE LIST
//
// Three things read the same list: the registrations below, the removal loop, and the log line at
// start-up. Three copies would drift, and the one that drifts silently is the removal loop, because a
// job whose identifier is missing there stays scheduled under the test suite.
//
// The list itself lives in the application layer, so these registrations and the administrator
// dashboard's health tile cannot disagree about what this application schedules. The test that asserts
// suppression deliberately keeps its own copy, for the reason recorded there: a test reading the same
// source cannot catch a job added or removed without anybody noticing.
//
//
// WHY THE JOB MANAGER IS RESOLVED RATHER THAN USED STATICALLY
//
// The static way writes to process-wide storage, so in a test process running more than one host the
// first host to start wins and every later one silently registers into that host's storage, schema
// override and all. Proven rather than assumed: with the static way, a host configured for its own
// schema created that schema and put all five jobs in the shared one. Behaviourally identical for the
// single-host production case.
//
//
// WHY SKIPPING REGISTRATION IS NOT ENOUGH
//
// The library persists these definitions in the database, so a definition written by an earlier run
// against the same database would still be picked up and fired by this host's server. Removing them
// makes the suppression true of the storage rather than only of this start-up.
//
//
// WHY THE STATE IS LOGGED
//
// A misconfiguration here is otherwise invisible. The switch defaults to on, so nothing changes by
// default, but a typo in its name in a deployed environment silently stops the tender timeline job, and
// submission windows then never open and never close. Tenders quietly stop working with no error
// anywhere.
//
// No test can catch that: a test asserting the correct name passes whether or not the deployed
// configuration uses the same one. Logging it makes the state visible at start-up rather than inferable
// later from the absence of behaviour. It is a mitigation rather than a fix, because it makes the
// failure loud rather than impossible.

namespace MotsSupplierPortal.Api.Startup;

using Hangfire;
using MotsSupplierPortal.Infrastructure.Awards;
using MotsSupplierPortal.Infrastructure.Suppliers;

internal static class RecurringJobRegistration
{
    internal static WebApplication ScheduleRecurringJobs(this WebApplication app)
    {
        string[] RecurringJobIds = MotsSupplierPortal.Application.Admin.RecurringJobs.All;

        var recurringJobsEnabled = app.Configuration.GetValue("Jobs:EnableRecurring", defaultValue: true);

        var recurringJobs = app.Services.GetRequiredService<IRecurringJobManager>();

        if (recurringJobsEnabled)
        {
            recurringJobs.AddOrUpdate<DocumentExpiryJob>(
                "document-expiry-lifecycle", job => job.RunAsync(CancellationToken.None), Cron.Daily);

            recurringJobs.AddOrUpdate<MotsSupplierPortal.Infrastructure.Registrations.DraftCleanupJob>(
                "draft-registration-cleanup", job => job.RunAsync(CancellationToken.None), Cron.Daily);

            recurringJobs.AddOrUpdate<MotsSupplierPortal.Infrastructure.Suppliers.OutboxDispatcher>(
                "outbox-dispatch", job => job.DispatchPendingAsync(CancellationToken.None), "*/5 * * * *");

            recurringJobs.AddOrUpdate<MotsSupplierPortal.Infrastructure.Rfqs.RfqTimelineJob>(
                "rfq-timeline", job => job.RunAsync(CancellationToken.None), "*/5 * * * *");

            recurringJobs.AddOrUpdate<AwardErpSyncJob>(
                "award-erp-sync", job => job.RunAsync(CancellationToken.None), "*/5 * * * *");

            recurringJobs.AddOrUpdate<MotsSupplierPortal.Infrastructure.Idempotency.IdempotencyCleanupJob>(
                "idempotency-cleanup", job => job.RunAsync(CancellationToken.None), Cron.Hourly);
        }
        else
        {
            foreach (var jobId in RecurringJobIds)
            {
                recurringJobs.RemoveIfExists(jobId);
            }
        }

        app.Logger.LogInformation(
            "Recurring jobs {RecurringJobsState}: {RecurringJobCount} scheduled ({RecurringJobIds})",
            recurringJobsEnabled ? "ENABLED" : "DISABLED",
            recurringJobsEnabled ? RecurringJobIds.Length : 0,
            recurringJobsEnabled ? string.Join(", ", RecurringJobIds) : "none");

        return app;
    }
}
