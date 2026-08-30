using Microsoft.Extensions.Diagnostics.HealthChecks;
using MotsSupplierPortal.Infrastructure.Storage;

namespace MotsSupplierPortal.Infrastructure.Observability;

/// <summary>
/// Task #16/NFR-OBS-006: readiness dimension #3 (docs/architecture/OBSERVABILITY-ARCHITECTURE.md
/// §5) - "object storage reachable". No existing S3-compatible health check package is
/// referenced, and this project has already found the third-party MinIO SDK to fail silently
/// against this MinIO version (MinioFileStorage's own doc comment) - a health check is exactly
/// the wrong place to add a second, unverified client that could report healthy over a
/// connection that does not actually work. Reuses the SAME AWSSDK.S3 client the app already
/// depends on for real uploads/downloads, via MinioFileStorage.PingAsync (read-only).
/// </summary>
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
