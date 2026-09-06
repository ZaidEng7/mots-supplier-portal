using Hangfire;
using Hangfire.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Admin;

/// <summary>
/// SCR-721's read. The admin overview already reports whether the expected jobs are REGISTERED; this
/// adds what an operator asks next - when each last ran, whether it worked, and when it runs again.
///
/// <para><b>Read from this host's JobStorage, not the static JobStorage.Current facade</b> - the same
/// reason GetAdminOverviewHandler gives: the static one is process-wide, and in a test process running
/// more than one host the first host wins.</para>
/// </summary>
public sealed class GetJobsMonitorHandler(JobStorage jobStorage, IConfiguration configuration) : IGetJobsMonitorHandler
{
    public JobsMonitorDto Handle()
    {
        using var connection = jobStorage.GetConnection();
        var registered = connection.GetRecurringJobs().ToDictionary(job => job.Id, StringComparer.Ordinal);

        // Driven by the EXPECTED list, not by what Hangfire happens to hold. A monitor that listed only
        // what exists could never show the one thing worth showing: a job that should be there and is
        // not. Anything registered but unexpected is appended, because an orphan left by an old
        // deployment is equally an operator's problem.
        var rows = RecurringJobs.All
            .Concat(registered.Keys.Where(id => !RecurringJobs.All.Contains(id)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .Select(id => registered.TryGetValue(id, out var job)
                ? new RecurringJobRowDto(id, true, job.Cron, job.LastExecution, job.NextExecution, job.LastJobState)
                : new RecurringJobRowDto(id, false, null, null, null, null))
            .ToList();

        return new JobsMonitorDto(configuration.GetValue("Jobs:EnableRecurring", defaultValue: true), rows);
    }
}

/// <summary>
/// SCR-721's one action: run this job now.
///
/// <para><b>There is no pause, deliberately.</b> Hangfire has no paused state for a recurring job - the
/// only way to stop one is to remove the registration, which would make "paused by an operator"
/// indistinguishable from "missing because a deployment dropped it". That second thing is precisely the
/// fault the health tile exists to detect, and a pause button would blind it. The global switch
/// (Jobs:EnableRecurring) remains the supported way to stop schedules, and the screen says so.</para>
/// </summary>
public sealed class TriggerRecurringJobHandler(JobStorage jobStorage, IRecurringJobManager jobs) : ITriggerRecurringJobHandler
{
    public bool Handle(string jobId)
    {
        using var connection = jobStorage.GetConnection();
        // Checked against the storage rather than against RecurringJobs.All: the question is whether
        // Hangfire can run it, not whether this application meant to register it.
        if (!connection.GetRecurringJobs().Any(job => string.Equals(job.Id, jobId, StringComparison.Ordinal)))
        {
            return false;
        }

        jobs.Trigger(jobId);
        return true;
    }
}

/// <summary>
/// SCR-722's read: the outbox, per message, with the payload an operator needs to judge a replay.
///
/// <para>The payload is integration data - what was already sent to an external system - and the
/// endpoint is system_admin only. It is returned whole rather than summarised because a message that
/// cannot be read cannot be judged, and the alternative is an operator replaying blind.</para>
/// </summary>
public sealed class GetOutboxMonitorHandler(AppDbContext db) : IGetOutboxMonitorHandler
{
    private const int PageSize = 100;

    public async Task<OutboxMonitorDto> HandleAsync(string? status, CancellationToken ct)
    {
        // Every status, including the ones with no rows. An operator who cannot see "Failed: 0" cannot
        // tell it from a count that failed to load.
        var counts = Enum.GetValues<OutboxSyncStatus>().ToDictionary(s => s.ToString(), _ => 0);
        var grouped = await db.OutboxMessages.AsNoTracking()
            .GroupBy(m => m.SyncStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        foreach (var group in grouped) counts[group.Status.ToString()] = group.Count;

        // §6.2's multi-value OR form, so ?status=Failed,Pending is "everything not yet delivered" -
        // the question an operator actually has. Parsed the same way the endpoint validated it; an
        // unrecognised token never reaches here, because the endpoint answers 422 first.
        var wanted = (status ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => Enum.TryParse<OutboxSyncStatus>(token, ignoreCase: false, out var parsed) ? parsed : (OutboxSyncStatus?)null)
            .Where(parsed => parsed is not null)
            .Select(parsed => parsed!.Value)
            .ToList();

        var query = db.OutboxMessages.AsNoTracking().AsQueryable();
        if (wanted.Count > 0)
        {
            query = query.Where(m => wanted.Contains(m.SyncStatus));
        }

        // Failed first, then oldest: the order an operator works in. A newest-first list buries the
        // message that has been stuck longest, which is the one that matters.
        var messages = await query
            .OrderBy(m => m.SyncStatus == OutboxSyncStatus.Failed ? 0 : 1)
            .ThenBy(m => m.CreatedAt)
            .Take(PageSize)
            .Select(m => new OutboxMessageRowDto(
                m.Id, m.Type, m.SyncStatus.ToString(), m.CreatedAt, m.ProcessedAt, m.PayloadJson))
            .ToListAsync(ct);

        return new OutboxMonitorDto(counts, messages);
    }
}

/// <summary>
/// SCR-722's action: put a message back in the queue.
///
/// <para><b>Only a Failed message is replayable.</b> A Pending one is already going to be attempted, and
/// re-queuing a Sent one would send an integration event twice - the outbox exists to make delivery
/// exactly-once, and an admin button that breaks that is worse than no button. The guard is the reason
/// this handler exists rather than a bare ExecuteUpdate.</para>
///
/// <para>Replay resets the row to Pending and clears ProcessedAt; the dispatcher picks it up on its next
/// pass. It does NOT dispatch inline: doing so would put an external call on an HTTP request thread and
/// give the operator a timeout instead of an answer.</para>
/// </summary>
public sealed class ReplayOutboxMessageHandler(AppDbContext db) : IReplayOutboxMessageHandler
{
    public async Task<bool> HandleAsync(Guid id, CancellationToken ct)
    {
        var message = await db.OutboxMessages.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (message is null || message.SyncStatus != OutboxSyncStatus.Failed) return false;

        message.SyncStatus = OutboxSyncStatus.Pending;
        message.ProcessedAt = null;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
