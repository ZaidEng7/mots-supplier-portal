using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Observability;

/// <summary>
/// Task #16/NFR-OBS-006: readiness dimension #2 (docs/architecture/OBSERVABILITY-ARCHITECTURE.md
/// §5) - "migrations applied", distinct from #1's plain "PostgreSQL connectivity"
/// (AddNpgSql). A connection can succeed against a schema this app version does not actually
/// match (a rollback deployed against a database mid-forward-migration, or a migration that
/// failed partway) - readiness has to mean "can this replica correctly serve traffic against the
/// database it is actually connected to", not just "can it open a socket".
/// </summary>
public sealed class MigrationsAppliedHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();

        return pending.Count == 0
            ? HealthCheckResult.Healthy("All migrations applied.")
            : HealthCheckResult.Unhealthy($"{pending.Count} pending migration(s): {string.Join(", ", pending)}");
    }
}
