// The readiness check for the object store.
//
// No existing health-check package for this kind of store is referenced, and this project has already found the
// store's own client to fail silently against this server version.
//
// A health check is exactly the wrong place to add a second, unverified client that could report healthy over a
// connection that does not actually work, so this reuses the SAME client the application depends on for real
// uploads and downloads, through its read-only probe.

namespace MotsSupplierPortal.Infrastructure.Observability;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using MotsSupplierPortal.Infrastructure.Storage;

public sealed class ObjectStorageHealthCheck(MinioFileStorage fileStorage) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await fileStorage.PingAsync(ct);
            return HealthCheckResult.Healthy("Object storage reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Object storage unreachable.", ex);
        }
    }
}
