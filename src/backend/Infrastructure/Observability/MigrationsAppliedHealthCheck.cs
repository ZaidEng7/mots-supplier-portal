// The readiness check that this replica's schema matches this build.
//
// Distinct from plain database connectivity. A connection can succeed against a schema this version does not
// actually match: a rollback deployed against a database mid-migration, or a migration that failed partway.
//
// Readiness has to mean "can this replica correctly serve traffic against the database it is connected to", not
// just "can it open a socket".

namespace MotsSupplierPortal.Infrastructure.Observability;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MotsSupplierPortal.Infrastructure.Persistence;

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
