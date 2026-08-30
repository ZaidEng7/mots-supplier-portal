using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Infrastructure.Observability;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Suppliers;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// Task #16: proves the dispatcher-shaped hole is actually closed against the real database - not
/// a mocked IOutboxTransport, the real LoggingOutboxTransport registered in Program.cs, run against
/// rows written the same way ReviewApplicationHandlers/ComplianceReTrigger actually write them.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class OutboxDispatcherTests(PostgresApiFixture fixture)
{
    private static OutboxMessage PendingMessage(string type = "TestEvent") => new()
    {
        Id = Guid.CreateVersion7(),
        Type = type,
        PayloadJson = "{\"probe\":true}",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Pending_rows_are_marked_Sent_after_a_dispatch_run()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var message = PendingMessage();
        db.OutboxMessages.Add(message);
        await db.SaveChangesAsync();

        var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();
        await dispatcher.DispatchPendingAsync();

        var reloaded = await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == message.Id);
        reloaded.SyncStatus.Should().Be(OutboxSyncStatus.Sent);
        reloaded.ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Already_Sent_rows_are_left_alone_on_a_later_run()
    {
        // The exact scenario a dispatcher without a WHERE-Pending clause would get wrong: a second
        // run must not touch what a previous run already finished.
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var message = PendingMessage();
        db.OutboxMessages.Add(message);
        await db.SaveChangesAsync();

        var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();
        await dispatcher.DispatchPendingAsync();

        var afterFirstRun = await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == message.Id);
        var processedAtAfterFirstRun = afterFirstRun.ProcessedAt;

        await Task.Delay(50); // so a wrongly-re-touched ProcessedAt would visibly differ
        await dispatcher.DispatchPendingAsync();

        var afterSecondRun = await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == message.Id);
        afterSecondRun.ProcessedAt.Should().Be(processedAtAfterFirstRun,
            "a row already Sent must not be re-processed on a later run");
    }

    [Fact]
    public async Task The_denominator_a_batch_leaves_no_pending_row_behind_under_the_batch_size()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // A tag unique to this test run, isolated from whatever else the shared fixture wrote -
        // the same isolation technique used throughout this arc (synthetic, per-test-run values).
        var tag = $"denominator-probe-{Guid.NewGuid():N}";
        const int seeded = 12;
        for (var i = 0; i < seeded; i++)
        {
            db.OutboxMessages.Add(PendingMessage($"{tag}_{i}"));
        }
        await db.SaveChangesAsync();

        var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();
        await dispatcher.DispatchPendingAsync();

        var stillPending = await db.OutboxMessages
            .Where(m => m.Type.StartsWith(tag) && m.SyncStatus == OutboxSyncStatus.Pending)
            .CountAsync();

        stillPending.Should().Be(0,
            $"seeded exactly {seeded} rows (well under OutboxDispatcher.BatchSize={OutboxDispatcher.BatchSize}) - " +
            "every one of them must be processed in a single run, not merely 'some'");
    }

    [Fact]
    public async Task Backlog_gauge_reflects_real_pending_rows_and_drops_once_they_are_dispatched()
    {
        // OutboxBacklogGauge is constructed eagerly at host startup (Program.cs) against the
        // shared fixture's own AppMetrics/Meter - this reads its ObservableGauge directly via a
        // real MeterListener (BCL), the same technique proven in AppMetricsTests, rather than
        // trusting that a singleton with no consumers actually got built.
        //
        // Deliberately dispatches the seeded rows and re-measures, not just "seed N, assert +N" -
        // a gauge counting ALL rows (Pending and Sent alike) instead of just Pending would ALSO
        // pass a seed-only assertion (adding rows increases either count identically), so that
        // version would not actually prove the gauge is scoped to the backlog. Found exactly this
        // gap by testing the assertion against a deliberately broken (Count() with no Pending
        // filter) gauge before finalizing this test - it passed, which is why this second
        // measurement exists.
        var metrics = fixture.Services.GetRequiredService<AppMetrics>();
        var values = new List<int>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == metrics.Meter.Name && instrument.Name == "mots.outbox.backlog")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<int>((instrument, measurement, tags, state) => values.Add(measurement));
        listener.Start();

        listener.RecordObservableInstruments();
        var baseline = values[^1];

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        const int seeded = 7;
        for (var i = 0; i < seeded; i++)
        {
            db.OutboxMessages.Add(PendingMessage($"gauge-probe-{i}"));
        }
        await db.SaveChangesAsync();

        listener.RecordObservableInstruments();
        var afterSeeding = values[^1];
        afterSeeding.Should().Be(baseline + seeded,
            "the gauge must reflect the real Pending count in the database - it increased by " +
            "exactly the number of Pending rows just added, no more, no less");

        var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();
        await dispatcher.DispatchPendingAsync();

        listener.RecordObservableInstruments();
        var afterDispatch = values[^1];
        afterDispatch.Should().Be(baseline,
            "once the seeded rows are Sent, the gauge must drop back to baseline - a gauge that " +
            "counts every row regardless of status would stay elevated here, which is exactly the " +
            "regression this second measurement exists to catch");
    }
}
