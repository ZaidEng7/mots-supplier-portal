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
// run's suspensions. A later review found a supplier that left while suspended, and was reactivated afterwards,
// never suspended for its absence, and an empty read marking every suspended supplier as gone. The one after that
// found the automatic reinstatement on a document approval lifting the sync's own suspensions, because it could not
// see them. The next found the same hole on the disabled path, where a disable landing on a suspended supplier left no
// trace, and a renewal approved while a mark held it back never being honoured once the mark cleared. Each is written
// so that the version it came from fails it.
//
// THE DOCUMENT TESTS GO THROUGH THE REVIEWER'S APPROVAL HANDLER, not through Reactivate, because the defect lived in
// how that handler decides whose suspension came last. The expiry is produced the way time produces it: a document
// approved while in date, its date then written in storage, and the expiry job run.
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
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Infrastructure.Integration.Erp;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Suppliers;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class ErpSupplierSyncTests(PostgresApiFixture fixture) : IAsyncLifetime
{
    private const string Password = "Wattle-Harbour-Quince-72";
    private const string CommercialRegistration = "commercial_registration";
    private const string TaxCertificate = "tax_certificate";

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

    private async Task<SupplierSyncStatus> SyncStatusOfAsync(string externalId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Suppliers.AsNoTracking().SingleAsync(s => s.ExternalId == externalId)).SyncStatus;
    }

    private async Task SetLifecycleAsync(string externalId, Action<Supplier> change)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        change(await db.Suppliers.SingleAsync(s => s.ExternalId == externalId));
        await db.SaveChangesAsync();
    }

    private async Task ExpireAnAwardCriticalDocumentAsync(string externalId)
    {
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var supplierId = await db.Suppliers.Where(s => s.ExternalId == externalId).Select(s => s.Id).SingleAsync();
            var typeId = await db.DocumentTypes.Where(t => t.Code == CommercialRegistration).Select(t => t.Id).SingleAsync();
            var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);

            var document = SupplierDocument.CreatePendingScan(
                $"DOC-2026-{Guid.NewGuid().ToString("N")[..6]}", supplierId, typeId, 1, "quarantine/key",
                $"registration-{Guid.NewGuid():N}.pdf", "application/pdf", 2048, Guid.CreateVersion7(),
                issueDate: null, expiryDate: today.AddDays(1), expiryTracked: true, today: today);
            document.MarkScanClean("clean/key");
            document.Approve(Guid.CreateVersion7());
            db.SupplierDocuments.Add(document);
            await db.SaveChangesAsync();

            await db.Database.ExecuteSqlAsync(
                $"UPDATE supplier.supplier_document SET \"ExpiryDate\" = {today.AddDays(-1)} WHERE \"Id\" = {document.Id}");
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DocumentExpiryJob>().RunAsync(CancellationToken.None);
        }
    }

    private async Task AssertReinstatedAutomaticallyAsync(string externalId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplierId = await db.Suppliers.Where(s => s.ExternalId == externalId).Select(s => s.Id).SingleAsync();

        (await db.AuditLogs.AsNoTracking().AnyAsync(a => a.AggregateId == supplierId && a.Action == "supplier_auto_reinstated"))
            .Should().BeTrue("a reinstatement nobody can trace looks exactly like one somebody slipped through");
    }

    private async Task ApproveNewDocumentAsync(string externalId, string typeCode)
    {
        string documentCode;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var supplierId = await db.Suppliers.Where(s => s.ExternalId == externalId).Select(s => s.Id).SingleAsync();
            var typeId = await db.DocumentTypes.Where(t => t.Code == typeCode).Select(t => t.Id).SingleAsync();
            var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);

            var previous = await db.SupplierDocuments
                .Where(d => d.SupplierId == supplierId && d.DocumentTypeId == typeId && d.IsLatestVersion)
                .ToListAsync();
            foreach (var old in previous) old.SupersedeWithNewVersion();

            var document = SupplierDocument.CreatePendingScan(
                $"DOC-2026-{Guid.NewGuid().ToString("N")[..6]}", supplierId, typeId, previous.Count + 1,
                "quarantine/key", $"{typeCode}-{Guid.NewGuid():N}.pdf", "application/pdf", 2048, Guid.CreateVersion7(),
                issueDate: null, expiryDate: today.AddYears(1), expiryTracked: true, today: today);
            document.MarkScanClean("clean/key");
            db.SupplierDocuments.Add(document);
            await db.SaveChangesAsync();
            documentCode = document.ReferenceCode;
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var result = await new ApproveDocumentHandler(
                    scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                    new TestScope(Guid.CreateVersion7()),
                    scope.ServiceProvider.GetRequiredService<IAuditLogger>())
                .HandleAsync(documentCode, CancellationToken.None);

            result.Should().BeOfType<ReviewDocumentResult.Success>();
        }
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
    public async Task A_supplier_that_left_while_suspended_and_was_reactivated_later_is_suspended_once()
    {
        await SuspendEveryImportedSupplierAsync();

        var keep = Unique("ERP-MARKED-KEEP");
        var gone = Unique("ERP-MARKED-GONE");
        await RunAsync(new FixedSource(ErpRow(keep), ErpRow(gone)));
        await SetLifecycleAsync(gone, supplier => supplier.Suspend("Suspended by the ministry for cause."));

        await RunAsync(new FixedSource(ErpRow(keep)));
        (await SyncStatusOfAsync(gone)).Should().Be(SupplierSyncStatus.MarkedRemovedFromErp);

        await SetLifecycleAsync(gone, supplier => supplier.Reactivate("The matter is closed."));
        var afterReactivation = await RunAsync(new FixedSource(ErpRow(keep)));

        afterReactivation.Suspended.Should().Be(1);
        (await LifecycleOfAsync(gone)).Should().Be(
            SupplierLifecycleState.Suspended,
            "it was suspended for another reason when it left, so the sync never suspended it for its absence and "
            + "the person who reactivated it may not know; the first version shared the reinstated memory and left it "
            + "active and invitable");
        (await SyncStatusOfAsync(gone)).Should().Be(SupplierSyncStatus.RemovedFromErp);

        await SetLifecycleAsync(gone, supplier => supplier.Reactivate("Still works with us directly."));
        await RunAsync(new FixedSource(ErpRow(keep)));

        (await LifecycleOfAsync(gone)).Should().Be(
            SupplierLifecycleState.Active,
            "now a person has decided, and a job nobody watches must not undo that every night");
    }

    [Fact]
    public async Task A_sync_suspension_is_not_lifted_by_a_later_document_approval()
    {
        await SuspendEveryImportedSupplierAsync();

        var keep = Unique("ERP-DOC-KEEP");
        var gone = Unique("ERP-DOC-GONE");
        await RunAsync(new FixedSource(ErpRow(keep), ErpRow(gone)));

        await ExpireAnAwardCriticalDocumentAsync(gone);
        (await LifecycleOfAsync(gone)).Should().Be(SupplierLifecycleState.Suspended, "the control: the expiry rule acted");
        await ApproveNewDocumentAsync(gone, CommercialRegistration);
        (await LifecycleOfAsync(gone)).Should().Be(SupplierLifecycleState.Active, "the control: the renewal lifted it");

        await RunAsync(new FixedSource(ErpRow(keep)));
        (await LifecycleOfAsync(gone)).Should().Be(SupplierLifecycleState.Suspended);

        await ApproveNewDocumentAsync(gone, TaxCertificate);

        (await LifecycleOfAsync(gone)).Should().Be(
            SupplierLifecycleState.Suspended,
            "the last suspension is the sync's; the first version looked only for the expiry rule's rows and a "
            + "person's, found the old expiry row, and reinstated a supplier the ERP no longer has - which the sync "
            + "then read as a person's decision and never suspended again");
    }

    [Fact]
    public async Task A_supplier_that_left_the_erp_while_suspended_is_not_brought_back_by_a_document()
    {
        await SuspendEveryImportedSupplierAsync();

        var keep = Unique("ERP-DOC-MARK-KEEP");
        var gone = Unique("ERP-DOC-MARK-GONE");
        await RunAsync(new FixedSource(ErpRow(keep), ErpRow(gone)));

        await ExpireAnAwardCriticalDocumentAsync(gone);
        await RunAsync(new FixedSource(ErpRow(keep)));
        (await SyncStatusOfAsync(gone)).Should().Be(SupplierSyncStatus.MarkedRemovedFromErp);

        await ApproveNewDocumentAsync(gone, CommercialRegistration);

        (await LifecycleOfAsync(gone)).Should().Be(
            SupplierLifecycleState.Suspended,
            "a renewed document says nothing about whether Seven Gates still has the company; reinstating it made it "
            + "invitable for a night, with a 'you are reinstated' message, until the sync suspended it again");
    }

    [Fact]
    public async Task A_supplier_disabled_while_suspended_is_not_brought_back_by_a_document_and_a_person_is_overruled_once()
    {
        await SuspendEveryImportedSupplierAsync();

        var id = Unique("ERP-DISABLED-WHILE-SUSPENDED");
        await RunAsync(new FixedSource(ErpRow(id)));
        await ExpireAnAwardCriticalDocumentAsync(id);
        await RunAsync(new FixedSource(ErpRow(id) with { Disabled = true }));

        await ApproveNewDocumentAsync(id, CommercialRegistration);
        (await LifecycleOfAsync(id)).Should().Be(
            SupplierLifecycleState.Suspended,
            "the disable arrived while it was suspended; the second version kept no trace of it, so the renewal "
            + "reactivated a supplier Seven Gates had disabled, and no run ever suspended it for that");

        await SetLifecycleAsync(id, supplier => supplier.Reactivate("The matter is closed."));
        await RunAsync(new FixedSource(ErpRow(id) with { Disabled = true }));
        (await LifecycleOfAsync(id)).Should().Be(SupplierLifecycleState.Suspended, "suspended once for the disable");

        await SetLifecycleAsync(id, supplier => supplier.Reactivate("Still works with us directly."));
        await RunAsync(new FixedSource(ErpRow(id) with { Disabled = true }));
        (await LifecycleOfAsync(id)).Should().Be(SupplierLifecycleState.Active, "and a person's decision after that stands");
    }

    [Fact]
    public async Task A_renewal_approved_while_a_supplier_was_missing_is_honoured_when_the_erp_offers_it_again()
    {
        await SuspendEveryImportedSupplierAsync();

        var keep = Unique("ERP-RETURN-KEEP");
        var back = Unique("ERP-RETURN-BACK");
        await RunAsync(new FixedSource(ErpRow(keep), ErpRow(back)));
        await ExpireAnAwardCriticalDocumentAsync(back);
        await RunAsync(new FixedSource(ErpRow(keep)));

        await ApproveNewDocumentAsync(back, CommercialRegistration);
        (await LifecycleOfAsync(back)).Should().Be(SupplierLifecycleState.Suspended, "the control: the mark held it back");

        var report = await RunAsync(new FixedSource(ErpRow(keep), ErpRow(back)));

        (await LifecycleOfAsync(back)).Should().Be(
            SupplierLifecycleState.Active,
            "its documents are fixed and the ERP has it again; the approval was the only trigger and had passed, so "
            + "without a second look it stayed locked out of tenders until somebody noticed");
        report.Rows.Single(r => r.ExternalId == back).Notes.Should().ContainMatch("*reinstated automatically*");
        await AssertReinstatedAutomaticallyAsync(back);
    }

    [Fact]
    public async Task A_renewal_approved_while_a_supplier_was_disabled_is_honoured_when_the_erp_re_enables_it()
    {
        await SuspendEveryImportedSupplierAsync();

        var id = Unique("ERP-REENABLED");
        await RunAsync(new FixedSource(ErpRow(id)));
        await ExpireAnAwardCriticalDocumentAsync(id);
        await RunAsync(new FixedSource(ErpRow(id) with { Disabled = true }));
        await ApproveNewDocumentAsync(id, CommercialRegistration);
        (await LifecycleOfAsync(id)).Should().Be(SupplierLifecycleState.Suspended, "the control: the mark held it back");

        await RunAsync(new FixedSource(ErpRow(id)));

        (await LifecycleOfAsync(id)).Should().Be(SupplierLifecycleState.Active);
        await AssertReinstatedAutomaticallyAsync(id);
    }

    [Fact]
    public async Task A_supplier_the_sync_suspended_is_not_reinstated_when_the_erp_offers_it_again()
    {
        await SuspendEveryImportedSupplierAsync();

        var id = Unique("ERP-NOT-REINSTATED");
        await RunAsync(new FixedSource(ErpRow(id)));
        await RunAsync(new FixedSource(ErpRow(id) with { Disabled = true }));
        await ExpireAnAwardCriticalDocumentAsync(id);

        await RunAsync(new FixedSource(ErpRow(id)));

        (await LifecycleOfAsync(id)).Should().Be(
            SupplierLifecycleState.Suspended,
            "the second look is only for a reinstatement a mark held back; lifting the sync's own suspension is a "
            + "person's decision");
    }

    [Fact]
    public async Task An_empty_list_from_the_erp_marks_no_suspended_supplier_as_gone()
    {
        await SuspendEveryImportedSupplierAsync();

        var id = Unique("ERP-EMPTY-MARK");
        await RunAsync(new FixedSource(ErpRow(id)));
        await SetLifecycleAsync(id, supplier => supplier.Suspend("Suspended by the ministry for cause."));

        var report = await RunAsync(new FixedSource());

        report.SuspensionsHeldBack.Should().Contain("returned no suppliers");
        (await SyncStatusOfAsync(id)).Should().Be(
            SupplierSyncStatus.Synced,
            "the first version judged only active suppliers, so with none missing it believed the empty read and "
            + "marked every suspended supplier as gone");
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
    public async Task A_suspended_supplier_renamed_in_the_erp_does_not_come_back_as_a_new_active_one()
    {
        await SuspendEveryImportedSupplierAsync();

        var keep = Unique("ERP-SUSP-RENAME-KEEP");
        var oldId = Unique("ERP-SUSP-RENAME-OLD");
        var newId = Unique("ERP-SUSP-RENAME-NEW");
        var taxId = $"TAX-{Guid.CreateVersion7():N}"[..16];

        await RunAsync(new FixedSource(ErpRow(keep), ErpRow(oldId) with { TaxId = taxId }));

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Suppliers.SingleAsync(s => s.ExternalId == oldId)).Suspend("Suspended by the ministry for cause.");
            await db.SaveChangesAsync();
        }

        var report = await RunAsync(new FixedSource(ErpRow(keep), ErpRow(newId) with { TaxId = taxId }));

        report.Created.Should().Be(
            0,
            "the second version created it as a brand-new active supplier, silently undoing the ministry's suspension");

        await using var check = fixture.Services.CreateAsyncScope();
        var checkDb = check.ServiceProvider.GetRequiredService<AppDbContext>();
        (await checkDb.Suppliers.CountAsync(s => s.ExternalId == newId)).Should().Be(0);
        (await LifecycleOfAsync(oldId)).Should().Be(SupplierLifecycleState.Suspended);
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
