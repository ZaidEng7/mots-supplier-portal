// The instrument that makes a stuck outbox dispatcher visible.
//
// A dispatcher running correctly and one silently stuck, with a transport throwing every time and nobody
// watching the job list, look identical from outside: pending rows simply stop decreasing.
//
// It is a gauge rather than a counter, because a backlog is a level, how many right now, rather than
// something that only goes up.
//
// It is constructed eagerly at startup so the callback is wired up whether or not anything else happens to
// resolve this type. A gauge nobody ever constructed is exactly the pathology of an instrument reporting
// over an absent set, at the level of object lifetime rather than of a query.
//
// Each observation opens its own short-lived scope rather than capturing a database context. The callback
// fires on the metrics library's own collection cycle, which long outlives any one request's context.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Infrastructure.Observability;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class OutboxBacklogGauge
{
    public OutboxBacklogGauge(AppMetrics metrics, IServiceScopeFactory scopeFactory)
    {
        metrics.Meter.CreateObservableGauge(
            "mots.outbox.backlog",
            () =>
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                return db.OutboxMessages.Count(m => m.SyncStatus == OutboxSyncStatus.Pending);
            },
            unit: "{message}",
            description: "Outbox rows still Pending - a dispatcher stuck or falling behind shows up as this not decreasing.");
    }
}
