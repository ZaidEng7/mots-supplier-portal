// The jobs monitor: when each scheduled job last ran, whether it worked, and when it runs again.
//
// The overview already reports whether the expected jobs are REGISTERED. This adds what an operator asks next.
//
// It is driven by the EXPECTED list rather than by what the scheduler happens to hold. A monitor listing only
// what exists could never show the one thing worth showing: a job that should be there and is not.
//
// Anything registered but unexpected is appended, because an orphan left by an old deployment is equally an
// operator's problem.
//
// Read from this host's own storage rather than the process-wide static facade, for the reason the overview
// gives.

namespace MotsSupplierPortal.Infrastructure.Admin;

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

public sealed class GetJobsMonitorHandler(JobStorage jobStorage, IConfiguration configuration) : IGetJobsMonitorHandler
{
    public JobsMonitorDto Handle()
    {
        using var connection = jobStorage.GetConnection();
        var registered = connection.GetRecurringJobs().ToDictionary(job => job.Id, StringComparer.Ordinal);

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
