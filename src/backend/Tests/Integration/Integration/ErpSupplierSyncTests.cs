// Keeping the portal's suppliers in step with the ERP, night after night.
//
// THE EMPTY-LIST TEST IS THE ONE THAT PROTECTS THE MINISTRY. A narrowed credential or an ERP answering politely with
// nothing returns zero suppliers, and a job that believed it would suspend every imported supplier at two in the
// morning. It is run against a portal that really holds imported suppliers, because that is when believing it does
// the damage, and it checks the database rather than the report - a report can say "held back" while a bug
// suspends anyway.
//
// THE SELF-REGISTERED SUPPLIER IS NEVER A CANDIDATE. It carries no ERP identifier and is never in the ERP's list,
// so a pass that treated absence as deletion without that distinction would suspend every supplier who ever signed
// up on the portal. It is asserted by building one and checking it is still active afterwards.
//
// THE LOCK IS TESTED BY HOLDING IT, not by racing two runs and hoping they overlap. A test that raced them would pass
// whenever the timing happened not to collide, which is the failure it exists to catch.
//
// EVERY TEST USES ITS OWN IDENTIFIERS AND LEAVES THE CONNECTION ROW AS IT FOUND IT, because the row and the
// supplier table are shared across the collection - a lesson this suite already paid for once.

namespace MotsSupplierPortal.Tests.Integration.Integration;

using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Integration;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Integration;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Integration.Erp;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class ErpSupplierSyncTests(PostgresApiFixture fixture) : IAsyncLifetime
{
    private const string Password = "Wattle-Harbour-Quince-72";

    public Task InitializeAsync() => IntegrationConnectionTests.ResetAsync(fixture);

    public Task DisposeAsync() => IntegrationConnectionTests.ResetAsync(fixture);

    private sealed class FixedSource(params ErpSupplier[] suppliers) : IErpSupplierSource
    {
        public Task<IReadOnlyList<ErpSupplier>> ListSuppliersAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ErpSupplier>>(suppliers);
    }

    private sealed class FixedConnection(ErpConnection? connection) : IErpConnectionProvider
    {
        public Task<ErpConnection?> CurrentAsync(CancellationToken ct) => Task.FromResult(connection);
    }

    private static ErpSupplier ErpRow(string id) =>
        new(id, id, "Local", "Company", null, "Syria", $"{id.ToLowerInvariant()}@sgtest.example", null, false,
            "SYP", null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static string Unique(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}";

    private static RunErpImportHandler Handler(AsyncServiceScope scope, IErpSupplierSource source) =>
        new(
            source,
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(),
            Options.Create(new ErpImportOptions { InitialPassword = Password }),
            scope.ServiceProvider.GetRequiredService<IAuditLogger>(),
            NullLogger<RunErpImportHandler>.Instance);

    private async Task<ErpImportRunReport> RunAsync(IErpSupplierSource source)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        return await Handler(scope, source).HandleAsync(ErpImportTrigger.Manual, CancellationToken.None);
    }

    private async Task<SupplierLifecycleState> LifecycleOfAsync(string externalId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Suppliers.AsNoTracking().SingleAsync(s => s.ExternalId == externalId)).LifecycleState;
    }

    // The whole portal's imported suppliers take part in every run, so each test first suspends any that earlier
    // tests left active. Otherwise a test's "missing" count would include strangers and the policy would be judging
    // a number this test did not choose.
    private async Task SuspendEveryImportedSupplierAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var supplier in await db.Suppliers
                     .Where(s => s.ExternalId != null && s.LifecycleState == SupplierLifecycleState.Active)
                     .ToListAsync())
        {
            supplier.Suspend("Isolating an ERP sync test.");
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_supplier_the_erp_no_longer_returns_is_suspended()
    {
        await SuspendEveryImportedSupplierAsync();

        var kept = Unique("ERP-KEPT");
        var removed = Unique("ERP-REMOVED");
        var first = await RunAsync(new FixedSource(ErpRow(kept), ErpRow(removed)));
        first.Created.Should().Be(2);

        var second = await RunAsync(new FixedSource(ErpRow(kept)));

        second.Suspended.Should().Be(1);
        second.SuspensionsHeldBack.Should().BeNull();
        (await LifecycleOfAsync(removed)).Should().Be(SupplierLifecycleState.Suspended);
        (await LifecycleOfAsync(kept)).Should().Be(SupplierLifecycleState.Active);
    }

    [Fact]
    public async Task An_empty_list_from_the_erp_suspends_nobody()
    {
        await SuspendEveryImportedSupplierAsync();

        var a = Unique("ERP-EMPTY-A");
        var b = Unique("ERP-EMPTY-B");
        await RunAsync(new FixedSource(ErpRow(a), ErpRow(b)));

        var report = await RunAsync(new FixedSource());

        report.Suspended.Should().Be(0);
        report.SuspensionsHeldBack.Should().Contain("returned no suppliers");
        (await LifecycleOfAsync(a)).Should().Be(
            SupplierLifecycleState.Active,
            "an ERP does not lose every supplier overnight; believing it would suspend the whole registry");
        (await LifecycleOfAsync(b)).Should().Be(SupplierLifecycleState.Active);
    }

    [Fact]
    public async Task A_supplier_who_registered_on_the_portal_is_never_suspended_for_being_absent_from_the_erp()
    {
        await SuspendEveryImportedSupplierAsync();

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // An approved, active supplier with no ERP identifier - what a supplier who registered here and passed
            // review looks like. Built through the import factory and then stripped of its identifier, because the
            // registration path would need documents, a reviewer and nine state transitions to reach Active, none of
            // which this test is about. The only property under test is "no ERP identifier".
            var own = MotsSupplierPortal.Domain.Suppliers.Supplier.ImportFromErp(
                $"SUP-T-{Guid.CreateVersion7():N}"[..20], "stripped-below", "Registered Here", null,
                SupplierLegalType.Company, "SYP", "Registered Here", $"{Guid.CreateVersion7():N}@sgtest.example", null);
            db.Suppliers.Add(own);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlAsync(
                $"UPDATE supplier.supplier SET \"ExternalId\" = NULL WHERE \"Id\" = {own.Id}");

            await RunAsync(new FixedSource(ErpRow(Unique("ERP-OTHER"))));

            (await db.Suppliers.AsNoTracking().SingleAsync(s => s.Id == own.Id)).LifecycleState.Should().Be(
                SupplierLifecycleState.Active,
                "it carries no ERP identifier, so its absence from the ERP says nothing about it");
        }
    }

    [Fact]
    public async Task A_supplier_that_comes_back_is_not_reinstated()
    {
        await SuspendEveryImportedSupplierAsync();

        var keep = Unique("ERP-BACK-KEEP");
        var flicker = Unique("ERP-BACK");
        await RunAsync(new FixedSource(ErpRow(keep), ErpRow(flicker)));
        await RunAsync(new FixedSource(ErpRow(keep)));
        await RunAsync(new FixedSource(ErpRow(keep), ErpRow(flicker)));

        (await LifecycleOfAsync(flicker)).Should().Be(
            SupplierLifecycleState.Suspended,
            "reinstating is a person's decision - they may have been suspended here for a reason the ERP never knew");
    }

    [Fact]
    public async Task A_second_import_while_one_is_running_is_refused_rather_than_duplicating()
    {
        await using var holder = fixture.Services.CreateAsyncScope();
        var holderDb = holder.ServiceProvider.GetRequiredService<AppDbContext>();
        await holderDb.Database.OpenConnectionAsync();

        try
        {
            (await holderDb.Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock({7_346_815_201_001L}) AS \"Value\"")
                .SingleAsync()).Should().BeTrue("the test must actually hold the lock for this to prove anything");

            var act = () => RunAsync(new FixedSource(ErpRow(Unique("ERP-BUSY"))));

            await act.Should().ThrowAsync<ErpImportBusyException>(
                "two overlapping runs would both find the same new supplier missing and both create it");
        }
        finally
        {
            await holderDb.Database.SqlQuery<bool>($"SELECT pg_advisory_unlock({7_346_815_201_001L}) AS \"Value\"")
                .SingleAsync();
            await holderDb.Database.CloseConnectionAsync();
        }
    }

    [Fact]
    public async Task Every_run_is_recorded_on_the_connection_including_one_that_failed()
    {
        await RunAsync(new FixedSource(ErpRow(Unique("ERP-RECORDED"))));

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.IntegrationConnections.AsNoTracking().SingleAsync(c => c.Key == IntegrationConnection.ErpKey);
            row.LastSyncSucceeded.Should().BeTrue();
            row.LastSyncSummary.Should().Contain("created");
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var failing = new RunErpImportHandler(
                new FixedSource(),
                scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(),
                Options.Create(new ErpImportOptions { InitialPassword = null }),
                scope.ServiceProvider.GetRequiredService<IAuditLogger>(),
                NullLogger<RunErpImportHandler>.Instance);

            var act = () => failing.HandleAsync(ErpImportTrigger.Scheduled, CancellationToken.None);
            await act.Should().ThrowAsync<ErpImportNotConfiguredException>();
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.IntegrationConnections.AsNoTracking().SingleAsync(c => c.Key == IntegrationConnection.ErpKey);
            row.LastSyncSucceeded.Should().BeFalse(
                "a nightly failure nobody records looks exactly like a night with nothing to do");
            row.LastSyncSummary.Should().Contain("InitialPassword");
        }
    }

    [Fact]
    public async Task The_nightly_job_does_nothing_when_no_erp_is_configured()
    {
        var calls = 0;
        var import = new CountingImport(() => calls++);

        var job = new ErpSupplierSyncJob(new FixedConnection(null), import, NullLogger<ErpSupplierSyncJob>.Instance);
        await job.RunAsync();

        var disabled = new ErpSupplierSyncJob(
            new FixedConnection(new ErpConnection("http://x", "k", "s", IsEnabled: false, ErpConnectionSource.Database)),
            import,
            NullLogger<ErpSupplierSyncJob>.Instance);
        await disabled.RunAsync();

        calls.Should().Be(
            0,
            "a deployment that is not integrated yet is not failing every night, and recording that it is would "
            + "teach people to ignore the failure that eventually matters");
    }

    [Fact]
    public async Task The_nightly_job_steps_aside_when_another_import_is_running()
    {
        var job = new ErpSupplierSyncJob(
            new FixedConnection(new ErpConnection("http://x", "k", "s", IsEnabled: true, ErpConnectionSource.Database)),
            new CountingImport(() => throw new ErpImportBusyException()),
            NullLogger<ErpSupplierSyncJob>.Instance);

        var act = () => job.RunAsync();

        await act.Should().NotThrowAsync("the run already in progress is doing tonight's work");
    }

    private sealed class CountingImport(Action onRun) : IRunErpImportHandler
    {
        public Task<ErpImportRunReport> HandleAsync(ErpImportTrigger trigger, CancellationToken ct)
        {
            onRun();
            return Task.FromResult(new ErpImportRunReport(0, 0, 0, 0, 0, []));
        }
    }
}
