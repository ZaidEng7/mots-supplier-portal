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
// SEVERAL TESTS HERE ARE FINDINGS FROM TWO REVIEWS: the run judged its limit after creating that night's new
// suppliers and so could suspend what the preview promised to hold back; it re-suspended suppliers people had
// reinstated, first through the "removed" route and then through the "disabled" one; it treated an ERP rename as a
// deletion, and the first fix for that moved company histories between records; and it named nobody on a manual
// run's suspensions. Each is written so that the version it came from fails it.
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

    private static RunErpImportHandler Handler(AsyncServiceScope scope, IErpSupplierSource source, Guid? userId = null) =>
        new(
            source,
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(),
            Options.Create(new ErpImportOptions { InitialPassword = Password }),
            scope.ServiceProvider.GetRequiredService<IAuditLogger>(),
            new TestScope(userId),
            NullLogger<RunErpImportHandler>.Instance);

    private async Task<ErpImportRunReport> RunAsync(IErpSupplierSource source, Guid? userId = null)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        return await Handler(scope, source, userId).HandleAsync(ErpImportTrigger.Manual, CancellationToken.None);
    }

    private async Task<ErpImportPreviewReport> PreviewAsync(IErpSupplierSource source)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        return await new PreviewErpImportHandler(
                source,
                scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                scope.ServiceProvider.GetRequiredService<IAuditLogger>())
            .HandleAsync(CancellationToken.None);
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
    public async Task The_run_holds_back_exactly_when_the_preview_said_it_would_even_with_new_arrivals()
    {
        await SuspendEveryImportedSupplierAsync();

        var existing = Enumerable.Range(0, 20).Select(_ => Unique("ERP-ARRIVALS-OLD")).ToList();
        await RunAsync(new FixedSource([.. existing.Select(ErpRow)]));

        var arrivals = Enumerable.Range(0, 12).Select(_ => Unique("ERP-ARRIVALS-NEW")).ToList();
        var tonight = new FixedSource([.. existing.Skip(6).Select(ErpRow), .. arrivals.Select(ErpRow)]);

        var preview = await PreviewAsync(tonight);
        var run = await RunAsync(tonight);

        preview.WouldSuspend.Should().Be(0, "6 of 20 active is above the limit of 5");
        preview.SuspensionsHeldBack.Should().NotBeNull();

        run.Suspended.Should().Be(
            0,
            "the first version counted tonight's 12 new suppliers into the limit, which rose to 8 and let all 6 through "
            + "- the preview's promise must hold in the run");
        run.SuspensionsHeldBack.Should().Be(preview.SuspensionsHeldBack);

        foreach (var id in existing.Take(6))
        {
            (await LifecycleOfAsync(id)).Should().Be(SupplierLifecycleState.Active);
        }
    }

    [Fact]
    public async Task A_supplier_a_person_reinstated_is_not_suspended_again_by_the_next_run()
    {
        await SuspendEveryImportedSupplierAsync();

        var keep = Unique("ERP-REINSTATE-KEEP");
        var gone = Unique("ERP-REINSTATE-GONE");
        await RunAsync(new FixedSource(ErpRow(keep), ErpRow(gone)));
        await RunAsync(new FixedSource(ErpRow(keep)));
        (await LifecycleOfAsync(gone)).Should().Be(SupplierLifecycleState.Suspended);

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Suppliers.SingleAsync(s => s.ExternalId == gone)).Reactivate("Still works with us directly.");
            await db.SaveChangesAsync();
        }

        var next = await RunAsync(new FixedSource(ErpRow(keep)));

        next.Suspended.Should().Be(0);
        (await LifecycleOfAsync(gone)).Should().Be(
            SupplierLifecycleState.Active,
            "a person reinstated it; a job nobody watches must not undo that every night");
    }

    [Fact]
    public async Task A_probable_rename_is_held_for_a_person_and_nothing_is_suspended_created_or_moved()
    {
        await SuspendEveryImportedSupplierAsync();

        var keep = Unique("ERP-RENAME-KEEP");
        var oldId = Unique("ERP-RENAME-OLD");
        var newId = Unique("ERP-RENAME-NEW");
        var email = $"{oldId.ToLowerInvariant()}@sgtest.example";

        var first = await RunAsync(new FixedSource(ErpRow(keep), ErpRow(oldId) with { Email = email }));
        var reference = first.Rows.Single(r => r.ExternalId == oldId).ReferenceCode;

        var renamed = await RunAsync(new FixedSource(ErpRow(keep), ErpRow(newId) with { Email = email }));

        renamed.Suspended.Should().Be(0, "the old record carries on while a person checks");
        renamed.Created.Should().Be(0, "a second record for what may be the same company is not created");
        renamed.Rows.Single(r => r.ExternalId == newId).Notes.Should().ContainMatch("*Possibly the same company*");

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplier = await db.Suppliers.AsNoTracking().SingleAsync(s => s.ReferenceCode == reference);

        supplier.ExternalId.Should().Be(oldId, "nothing is moved: a wrong guess would put one company's history on another");
        supplier.LifecycleState.Should().Be(SupplierLifecycleState.Active);
        (await db.Suppliers.CountAsync(s => s.ExternalId == newId)).Should().Be(0);

        var connection = await db.IntegrationConnections.AsNoTracking().SingleAsync(c => c.Key == IntegrationConnection.ErpKey);
        connection.LastSyncOutcome.Should().Be(
            IntegrationSyncOutcome.NeedsAttention,
            "a rename waiting for a person must not show as a clean green run");
    }

    [Fact]
    public async Task A_contact_email_the_supplier_edited_is_not_mistaken_for_its_identity()
    {
        await SuspendEveryImportedSupplierAsync();

        var keep = Unique("ERP-CONTACT-KEEP");
        var mine = Unique("ERP-CONTACT-MINE");
        var stranger = Unique("ERP-CONTACT-STRANGER");
        var strangersAddress = $"{stranger.ToLowerInvariant()}@sgtest.example";

        await RunAsync(new FixedSource(ErpRow(keep), ErpRow(mine)));

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var supplier = await db.Suppliers.Include(s => s.Representatives).SingleAsync(s => s.ExternalId == mine);
            supplier.Representatives[0].Email = strangersAddress;
            await db.SaveChangesAsync();
        }

        var report = await RunAsync(new FixedSource(ErpRow(keep), ErpRow(stranger) with { Email = strangersAddress }));

        report.Rows.Single(r => r.ExternalId == stranger).Outcome.Should().Be(
            ErpImportOutcome.Created,
            "the contact address is editable by the supplier; matching on it would let one supplier claim whichever "
            + "company Seven Gates later adds with that address. Only the sign-in address identifies a supplier");
    }

    [Fact]
    public async Task A_supplier_disabled_in_the_erp_and_reinstated_by_a_person_is_not_suspended_again()
    {
        await SuspendEveryImportedSupplierAsync();

        var id = Unique("ERP-DISABLED-REINSTATED");
        await RunAsync(new FixedSource(ErpRow(id)));
        await RunAsync(new FixedSource(ErpRow(id) with { Disabled = true }));
        (await LifecycleOfAsync(id)).Should().Be(SupplierLifecycleState.Suspended);

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Suppliers.SingleAsync(s => s.ExternalId == id)).Reactivate("The ministry still works with them.");
            await db.SaveChangesAsync();

            var audited = await db.AuditLogs.AsNoTracking().AnyAsync(a =>
                a.Action == "supplier.suspended_disabled_in_erp"
                && a.AggregateId == db.Suppliers.Where(s => s.ExternalId == id).Select(s => s.Id).First());
            audited.Should().BeTrue("the suspension the ERP caused must say so in the audit trail");
        }

        await RunAsync(new FixedSource(ErpRow(id) with { Disabled = true }));

        (await LifecycleOfAsync(id)).Should().Be(
            SupplierLifecycleState.Active,
            "only a change from enabled to disabled suspends; a person's reinstatement is not undone every night");
    }

    [Fact]
    public async Task A_manual_run_names_the_person_who_ran_it_on_every_suspension()
    {
        await SuspendEveryImportedSupplierAsync();

        var keep = Unique("ERP-ACTOR-KEEP");
        var gone = Unique("ERP-ACTOR-GONE");
        var first = await RunAsync(new FixedSource(ErpRow(keep), ErpRow(gone)));

        Guid person;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            person = (await users.FindByEmailAsync($"{keep.ToLowerInvariant()}@sgtest.example"))!.Id;
        }

        await RunAsync(new FixedSource(ErpRow(keep)), userId: person);

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var supplierId = (await db.Suppliers.AsNoTracking().SingleAsync(s => s.ExternalId == gone)).Id;

            var row = await db.AuditLogs.AsNoTracking()
                .SingleAsync(a => a.AggregateId == supplierId && a.Action == "supplier.suspended_missing_from_erp");

            row.ActorUserId.Should().Be(
                person,
                "a suspension nobody can trace to the person who pressed the button is the kind somebody asks about");
        }
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
            row.LastSyncOutcome.Should().Be(IntegrationSyncOutcome.Succeeded);
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
                new TestScope(),
                NullLogger<RunErpImportHandler>.Instance);

            var act = () => failing.HandleAsync(ErpImportTrigger.Scheduled, CancellationToken.None);
            await act.Should().ThrowAsync<ErpImportNotConfiguredException>();
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.IntegrationConnections.AsNoTracking().SingleAsync(c => c.Key == IntegrationConnection.ErpKey);
            row.LastSyncOutcome.Should().Be(
                IntegrationSyncOutcome.Failed,
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

    private sealed class TestScope(Guid? userId = null) : IScopeContext
    {
        public Guid? UserId { get; } = userId;
        public Guid? SupplierId => null;
        public Guid? OrganizationId => null;
        public bool IsAuthenticated => UserId is not null;
        public bool HasPermission(string permission) => true;
    }
}
