// The readiness check for the background-job storage.
//
// It happens to be the same database instance the plain connectivity check already probes, but it is the job
// framework's OWN connection and schema rather than the application's, which the written architecture calls out as
// a distinct failure mode.
//
// Every verification, reminder and notification email depends on that framework specifically staying reachable,
// independently of whether the application's own connection is fine.
//
// The probe is a lightweight read-only round trip against its storage. No job data is created or changed.

namespace MotsSupplierPortal.Infrastructure.Observability;

using Hangfire;
using Microsoft.Extensions.Diagnostics.HealthChecks;

public sealed class HangfireStorageHealthCheck(JobStorage jobStorage) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            jobStorage.GetMonitoringApi().GetStatistics();
            return Task.FromResult(HealthCheckResult.Healthy("Hangfire storage reachable."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Hangfire storage unreachable.", ex));
        }
    }
}
