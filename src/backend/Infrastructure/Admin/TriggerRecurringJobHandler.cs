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
