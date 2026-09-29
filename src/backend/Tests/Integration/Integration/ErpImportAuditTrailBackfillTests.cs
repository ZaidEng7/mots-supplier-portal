// The migration that writes the audit rows the ERP import did not write for suppliers it had already created.
//
// THE MIGRATION'S OWN STATEMENT IS RUN, NOT A COPY. It is a constant on the migration for this reason: a copy here
// would keep passing after the migration changed.
//
// THE OLD WORLD IS REBUILT DELIBERATELY. On a fresh database every supplier is created by today's import, which writes
// its rows, so the backfill would match nothing and passing would prove only that it did not crash. So the suppliers
// are made the way the old import made them - through the same factory, saved with no row - and dated in the past,
// with the run rows around them placed by hand, in the shapes the real runs wrote: a person's, the hourly job's, and
// the import before the sync, which recorded nobody. The review found that last shape missing here, and with it every
// real supplier about to be credited, forever, to a job that did not exist when they were imported.
//
// THE RUN IT NAMES IS THE LATEST RUN BEFORE THE SUPPLIER, and the neighbours are there to prove it: an earlier run by
// somebody else, a later one, and a different action by a third person in between. A statement that took the first
// run, any run, or any row names the wrong person.
//
// THE ARRIVAL IS READ FROM WHAT IS STILL TRUE. A supplier the ERP re-enabled after it arrived disabled is still out of
// service for that arrival, and one that arrived waiting and was disabled later did not arrive disabled.
//
// THE TIMES ARE FIXED AND FAR IN THE PAST, and each test uses its own, because the suite shares one database and every
// other test's runs are dated now. Nothing another test writes can then fall between a run and its suppliers.
//
// IT IS RUN TWICE. The second run adds nothing, which is also what keeps a supplier that already has its row - whether
// from today's import or from NightlyErpSync - from getting a second one.

namespace MotsSupplierPortal.Tests.Integration.Integration;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Persistence.Migrations;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class ErpImportAuditTrailBackfillTests(PostgresApiFixture fixture)
{
    private static async Task<Supplier> ImportedSilentlyAsync(AppDbContext db, ErpStanding standing, DateTimeOffset createdAt)
    {
        var supplier = Supplier.ImportFromErp(
            $"SUP-T-{Guid.NewGuid():N}"[..24], $"ERP-OLD-{Guid.NewGuid():N}", "Imported Silently", null,
            SupplierLegalType.Company, "SYP", "Imported Silently", $"{Guid.NewGuid():N}@sgtest.example", null, standing);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlAsync(
            $"UPDATE supplier.supplier SET \"CreatedAt\" = {createdAt} WHERE \"Id\" = {supplier.Id}");
        return supplier;
    }

    private static AuditLog Row(DateTimeOffset at, string action, Guid? person = null, string? label = null,
        Guid? aggregateId = null, string? toState = null, string? ipAddress = null, Guid? correlation = null) => new()
    {
        Id = Guid.CreateVersion7(),
        OccurredAt = at,
        ActorKind = person is null ? AuditActorKind.System : AuditActorKind.User,
        ActorUserId = person,
        ActorLabel = label,
        AggregateType = "Supplier",
        AggregateId = aggregateId ?? Guid.Empty,
        Action = action,
        ToState = toState,
        CorrelationId = correlation ?? Guid.CreateVersion7(),
        IpAddress = ipAddress,
    };

    private static async Task BackfillAsync(AppDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(ErpImportAuditTrail.Backfill);
        await db.Database.ExecuteSqlRawAsync(ErpImportAuditTrail.Backfill);
    }

    private static Task<List<AuditLog>> TrailAsync(AppDbContext db, Guid supplierId) =>
        db.AuditLogs.AsNoTracking().Where(a => a.AggregateId == supplierId).ToListAsync();

    [Fact]
    public async Task Each_supplier_the_old_import_made_gets_its_arrival_named_after_the_run_that_made_it()
    {
        var runAt = new DateTimeOffset(2021, 4, 7, 10, 0, 0, TimeSpan.Zero);
        var createdAt = runAt.AddSeconds(2);
        var person = Guid.CreateVersion7();
        var correlation = Guid.CreateVersion7();

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.AuditLogs.AddRange(
            Row(runAt.AddHours(-1), "ErpImportRun", person: Guid.CreateVersion7()),
            Row(runAt, "ErpImportRun", person: person, ipAddress: "10.20.30.40", correlation: correlation),
            Row(runAt.AddSeconds(1), "staff_invited", person: Guid.CreateVersion7()),
            Row(createdAt.AddHours(1), "ErpImportRun", person: Guid.CreateVersion7()));
        await db.SaveChangesAsync();

        var usable = (await ImportedSilentlyAsync(db, ErpStanding.Usable, createdAt)).Id;
        var disabled = (await ImportedSilentlyAsync(db, ErpStanding.Disabled, createdAt)).Id;
        var pending = (await ImportedSilentlyAsync(db, ErpStanding.AwaitingApproval, createdAt)).Id;

        await BackfillAsync(db);

        foreach (var supplierId in new[] { usable, disabled, pending })
        {
            var imported = (await TrailAsync(db, supplierId)).Should()
                .ContainSingle(a => a.Action == "supplier.imported_from_erp", "a second run of the backfill adds nothing")
                .Subject;
            imported.OccurredAt.Should().Be(createdAt, "the row records when the supplier arrived, not when this ran");
            imported.ActorUserId.Should().Be(
                person,
                "the latest run before the supplier made it - not the earlier run, not the later one, and not the "
                + "unrelated action in between");
            imported.ActorKind.Should().Be(AuditActorKind.User);
            imported.CorrelationId.Should().Be(correlation, "it belongs to that run, and says so");
            imported.IpAddress.Should().Be("10.20.30.40");
            imported.ToState.Should().Be("Approved");
        }

        (await TrailAsync(db, usable)).Should().NotContain(
            a => a.ToState == "Suspended", "the control: a supplier that arrived in service gets no suspension");

        var disabledSuspension = (await TrailAsync(db, disabled)).Should().ContainSingle(a => a.ToState == "Suspended").Subject;
        disabledSuspension.Action.Should().Be("supplier.suspended_disabled_in_erp");
        disabledSuspension.OccurredAt.Should().Be(createdAt.AddTicks(10), "one microsecond after it arrived");
        disabledSuspension.Reason.Should().StartWith("Disabled in the ERP; it arrived suspended.");
        disabledSuspension.ActorUserId.Should().Be(person);

        (await TrailAsync(db, pending)).Should().ContainSingle(a => a.ToState == "Suspended")
            .Which.Action.Should().Be(
                "supplier.suspended_not_approved_in_erp",
                "filed under 'disabled', every report counting suppliers Seven Gates disabled would count this one");
    }

    [Fact]
    public async Task A_run_that_recorded_nobody_is_not_credited_to_the_hourly_job()
    {
        var beforeTheSync = new DateTimeOffset(2021, 8, 10, 9, 0, 0, TimeSpan.Zero);
        var hourly = beforeTheSync.AddHours(1);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.AuditLogs.AddRange(
            Row(beforeTheSync, "ErpImportRun", ipAddress: "10.9.8.7"),
            Row(hourly, "ErpImportRun", label: "system"));
        await db.SaveChangesAsync();

        var pressed = (await ImportedSilentlyAsync(db, ErpStanding.Usable, beforeTheSync.AddSeconds(2))).Id;
        var scheduled = (await ImportedSilentlyAsync(db, ErpStanding.Usable, hourly.AddSeconds(2))).Id;

        await BackfillAsync(db);

        var byAPerson = (await TrailAsync(db, pressed)).Should().ContainSingle().Subject;
        byAPerson.ActorLabel.Should().Be(
            "not recorded",
            "only a person could run the import before the sync, and it recorded nobody; 'system' would credit the "
            + "hourly job, permanently, with suppliers it never created");
        byAPerson.IpAddress.Should().Be("10.9.8.7", "the one trace left of who asked");

        (await TrailAsync(db, scheduled)).Should().ContainSingle()
            .Which.ActorLabel.Should().Be("system", "the control: a run the hourly job made says so");
    }

    [Fact]
    public async Task A_supplier_whose_standing_changed_since_it_arrived_is_recorded_as_it_arrived()
    {
        var createdAt = new DateTimeOffset(2021, 10, 5, 7, 0, 0, TimeSpan.Zero);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.AuditLogs.Add(Row(createdAt.AddSeconds(-2), "ErpImportRun", person: Guid.CreateVersion7()));
        await db.SaveChangesAsync();

        var reEnabled = await ImportedSilentlyAsync(db, ErpStanding.Disabled, createdAt);
        var withdrawn = await ImportedSilentlyAsync(db, ErpStanding.AwaitingApproval, createdAt);
        reEnabled.RecordErpStanding(ErpStanding.Usable);
        withdrawn.RecordErpStanding(ErpStanding.Disabled).Should().Be(ErpDisabledChange.ReleaseWithdrawn);
        db.AuditLogs.Add(Row(createdAt.AddHours(1), "supplier.erp_release_withdrawn", aggregateId: withdrawn.Id));
        await db.SaveChangesAsync();

        reEnabled.ErpDisabledState.Should().Be(
            SupplierErpDisabledState.NotDisabled, "the control: the ERP no longer disables it, yet it is still out");
        reEnabled.LifecycleState.Should().Be(SupplierLifecycleState.Suspended);

        await BackfillAsync(db);

        (await TrailAsync(db, reEnabled.Id)).Should().ContainSingle(a => a.ToState == "Suspended")
            .Which.Action.Should().Be(
                "supplier.suspended_disabled_in_erp",
                "it is still suspended for arriving disabled, and without the row nothing says why");
        (await TrailAsync(db, withdrawn.Id)).Should().ContainSingle(a => a.ToState == "Suspended")
            .Which.Action.Should().Be(
                "supplier.suspended_not_approved_in_erp",
                "it arrived waiting for approval; the disable came later and has its own row");
    }

    [Fact]
    public async Task A_supplier_whose_trail_already_says_it_leaves_the_backfill_with_nothing_added()
    {
        var createdAt = new DateTimeOffset(2021, 6, 2, 8, 0, 0, TimeSpan.Zero);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.AuditLogs.Add(Row(createdAt.AddSeconds(-2), "ErpImportRun", label: "system"));
        await db.SaveChangesAsync();

        var recorded = (await ImportedSilentlyAsync(db, ErpStanding.AwaitingApproval, createdAt)).Id;
        db.AuditLogs.Add(Row(
            createdAt.AddHours(3), "supplier.suspended_not_approved_in_erp", label: "system", aggregateId: recorded,
            toState: "Suspended"));
        await db.SaveChangesAsync();

        var registeredHere = (await ImportedSilentlyAsync(db, ErpStanding.Usable, createdAt)).Id;
        await db.Database.ExecuteSqlAsync(
            $"UPDATE supplier.supplier SET \"ExternalId\" = NULL WHERE \"Id\" = {registeredHere}");

        await BackfillAsync(db);

        (await TrailAsync(db, recorded)).Where(a => a.ToState == "Suspended").Should().ContainSingle(
            "NightlyErpSync already wrote this suspension; a second one would claim it happened twice");
        (await TrailAsync(db, registeredHere)).Should().BeEmpty(
            "a supplier with no ERP identifier was never imported, and the trail must not say it was");
    }

    [Fact]
    public async Task A_supplier_with_no_run_before_it_is_not_credited_to_anybody()
    {
        var createdAt = new DateTimeOffset(2019, 2, 3, 12, 0, 0, TimeSpan.Zero);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var supplierId = (await ImportedSilentlyAsync(db, ErpStanding.Usable, createdAt)).Id;

        await BackfillAsync(db);

        var imported = (await TrailAsync(db, supplierId)).Should().ContainSingle().Subject;
        imported.ActorUserId.Should().BeNull();
        imported.ActorLabel.Should().Be("not recorded", "nothing says who ran it, and the row does not guess");
        imported.CorrelationId.Should().NotBeEmpty();
    }
}
