using Hangfire;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MotsSupplierPortal.Infrastructure.Observability;

/// <summary>
/// Task #16/NFR-OBS-006: readiness dimension #4 (docs/architecture/OBSERVABILITY-ARCHITECTURE.md
/// §5) - "Hangfire storage reachable". This app's Hangfire storage happens to be the same
/// Postgres instance the plain connectivity check already probes (UsePostgreSqlStorage in
/// Program.cs), but it is Hangfire's OWN connection/schema, not the app's AppDbContext - a
/// distinct failure mode the architecture doc calls out separately (verification/reminder/audit
/// emails all depend on Hangfire specifically staying reachable, independent of whether
/// AppDbContext's own connection is fine). GetMonitoringApi().GetStatistics() is a lightweight,
/// read-only round trip against Hangfire's storage layer - no job data is created or mutated.
/// </summary>
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
