using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Infrastructure.Observability;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>
/// Task #16/NFR-OBS-006: "include a way to observe backlog size" - the one thing a dispatcher
/// running correctly and a dispatcher silently stuck (transport throwing every time, Hangfire's own
/// job list not being watched) look identical from the outside without this: Pending rows simply
/// stop decreasing. An ObservableGauge, not a Counter - the backlog is a level (how many right now),
/// not something that only increases.
///
/// <para>Registered as a singleton constructed eagerly at startup (Program.cs) so the gauge
/// callback is wired up whether or not anything else in the app happens to resolve this type - an
/// ObservableGauge nobody ever constructed is exactly the "instrument reporting over an absent set"
/// pathology (MSP-83) this session keeps finding, just at object-lifetime level instead of a query.
/// Uses IServiceScopeFactory rather than a captured AppDbContext: the gauge callback can fire at
/// any time on the OTel SDK's own collection cycle, long outliving any one request's scoped
/// DbContext, so each observation opens its own short-lived scope.</para>
/// </summary>
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
