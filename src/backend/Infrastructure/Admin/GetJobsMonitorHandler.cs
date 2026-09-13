using Hangfire;
using Hangfire.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Suppliers;

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
