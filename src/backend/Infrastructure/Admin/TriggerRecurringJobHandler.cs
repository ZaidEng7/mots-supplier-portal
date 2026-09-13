// Running a scheduled job now.
//
//
// THERE IS NO PAUSE, DELIBERATELY
//
// The scheduler has no paused state for a recurring job. The only way to stop one is to remove its registration,
// which would make "paused by an operator" indistinguishable from "missing because a deployment dropped it".
//
// That second thing is precisely the fault the health tile exists to detect, and a pause button would blind it.
//
// The global switch remains the supported way to stop schedules, and the screen says so.
//
// The job is checked against the scheduler's storage rather than against this application's expected list,
// because the question is whether the scheduler can run it and not whether this application meant to register
// it.

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

public sealed class TriggerRecurringJobHandler(JobStorage jobStorage, IRecurringJobManager jobs) : ITriggerRecurringJobHandler
{
    public bool Handle(string jobId)
    {
        using var connection = jobStorage.GetConnection();
        if (!connection.GetRecurringJobs().Any(job => string.Equals(job.Id, jobId, StringComparison.Ordinal)))
        {
            return false;
        }

        jobs.Trigger(jobId);
        return true;
    }
}
