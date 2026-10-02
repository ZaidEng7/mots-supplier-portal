// Keeping the portal's suppliers in step with the ERP, run after run.
//
// THE EMPTY-LIST TEST IS THE ONE THAT PROTECTS THE MINISTRY. A narrowed credential or an ERP answering politely with
// nothing returns zero suppliers, and a job that believed it would suspend every imported supplier in a run nobody
// watches. It is run against a portal that really holds imported suppliers - two it imports itself - because that is
// when believing it does the damage, and it checks the database rather than the report - a report can say "held back"
// while a bug suspends anyway.
//
// THE SELF-REGISTERED SUPPLIER IS NEVER A CANDIDATE. It carries no ERP identifier and is never in the ERP's list,
// so a pass that treated absence as deletion without that distinction would suspend every supplier who ever signed
// up on the portal. It is asserted by building one and checking it is still active afterwards.
//
// MOST TESTS HERE PIN A DEFECT THAT A REVIEW, OR THE FIRST RUN AGAINST THE REAL ERP, FOUND, each written so that the
// version it came from fails it. Among the rules they hold: the limit is judged on the portal as it stood before this
// run's new suppliers; a person's reinstatement is not undone, whether the sync suspended for an absence, a disable or
// a missing approval; a probable rename is held for a person and no company's history moves between records; a
// supplier that left while suspended is suspended once for its absence if it is reactivated later; an empty read marks
// nobody as gone; a document approval never lifts a suspension the sync made, nor brings back a supplier the ERP no
// longer offers or has disabled, while a renewal approved meanwhile is honoured once the ERP offers it again; every
// supplier the import creates, and every suspension it makes, is on the audit trail with whoever ran it; and so is the
// run itself, opened and closed under that actor with its counts or its failure, while a run refused for the lock
// writes nothing and a closing row that cannot be written never hides the failure it records.
//
// THE DOCUMENT TESTS GO THROUGH THE REVIEWER'S APPROVAL HANDLER, not through Reactivate, because the defect lived in
// how that handler decides whose suspension came last. The expiry is produced the way time produces it: a document
// approved while in date, its date then written in storage, and the expiry job run.
//
// THE LOCK IS TESTED BY HOLDING IT, not by racing two runs and hoping they overlap. A test that raced them would pass
// whenever the timing happened not to collide, which is the failure it exists to catch.
//
// EVERY TEST STARTS WITH NO IMPORTED SUPPLIER IN SERVICE, USES ITS OWN IDENTIFIERS AND LEAVES THE CONNECTION ROW AS IT
// FOUND IT, because the row and the supplier table are shared across the collection and every run judges the whole
// table. InitializeAsync and DisposeAsync do the first and the last for every test, so a new test cannot forget them;
// a test that needs imported suppliers in service creates its own.

namespace MotsSupplierPortal.Tests.Integration.Integration;

using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Integration;
using MotsSupplierPortal.Domain.Audit;
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

    // The imported suppliers earlier tests left active are suspended before every test, because every run judges the
    // whole portal: otherwise a test's "missing" count would include strangers and the policy would be judging a number
    // this test did not choose.
    public async Task InitializeAsync()
    {
        await IntegrationConnectionTests.ResetAsync(fixture);
        await SuspendEveryImportedSupplierAsync();
    }

    public Task DisposeAsync() => IntegrationConnectionTests.ResetAsync(fixture);

    private sealed class FixedSource(params ErpSupplier[] suppliers) : IErpSupplierSource
    {
        public Task<IReadOnlyList<ErpSupplier>> ListSuppliersAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ErpSupplier>>(suppliers);

        public Task<IReadOnlyList<string>> ListSupplierGroupsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class InterruptedSource : IErpSupplierSource
    {
        public Task<IReadOnlyList<ErpSupplier>> ListSuppliersAsync(CancellationToken ct) =>
            throw new OperationCanceledException();

        public Task<IReadOnlyList<string>> ListSupplierGroupsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class FixedConnection(ErpConnection? connection) : IErpConnectionProvider
    {
        public Task<ErpConnection?> CurrentAsync(CancellationToken ct) => Task.FromResult(connection);
    }

    private static ErpSupplier ErpRow(string id) =>
        ErpSupplierTestFactory.Supplier(id) with { Email = $"{id.ToLowerInvariant()}@sgtest.example" };

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
                scope.ServiceProvider.GetRequiredService<IAuditLogger>(),
                new TestScope())
            .HandleAsync(CancellationToken.None);
    }

    private async Task<SupplierLifecycleState> LifecycleOfAsync(string externalId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Suppliers.AsNoTracking().SingleAsync(s => s.ExternalId == externalId)).LifecycleState;
    }

    private async Task<(uint RowVersion, DateTimeOffset UpdatedAt, DateTimeOffset? LastSyncedAt)> VersionOfAsync(
        string externalId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplier = await db.Suppliers.AsNoTracking().SingleAsync(s => s.ExternalId == externalId);
        return (supplier.RowVersion, supplier.UpdatedAt, supplier.LastSyncedAt);
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

    private async Task<string> UploadNewDocumentAsync(string externalId, string typeCode)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
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

        return document.ReferenceCode;
    }

    private async Task ApproveDocumentAsync(string documentCode)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var result = await new ApproveDocumentHandler(
                scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                new TestScope(Guid.CreateVersion7()),
                scope.ServiceProvider.GetRequiredService<IAuditLogger>())
            .HandleAsync(documentCode, CancellationToken.None);

        result.Should().BeOfType<ReviewDocumentResult.Success>();
    }

    private async Task ApproveNewDocumentAsync(string externalId, string typeCode) =>
        await ApproveDocumentAsync(await UploadNewDocumentAsync(externalId, typeCode));

    private async Task RejectDocumentAsync(string documentCode)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.SupplierDocuments.SingleAsync(d => d.ReferenceCode == documentCode))
            .Reject(Guid.CreateVersion7(), "The scan is unreadable.");
        await db.SaveChangesAsync();
    }

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

    private static async Task<List<MotsSupplierPortal.Domain.Audit.AuditLog>> TrailOfAsync(AppDbContext db, string externalId)
    {
        var supplierId = await db.Suppliers.Where(s => s.ExternalId == externalId).Select(s => s.Id).SingleAsync();
        return await db.AuditLogs.AsNoTracking().Where(a => a.AggregateId == supplierId).ToListAsync();
    }

    [Fact]
    public async Task A_supplier_the_erp_no_longer_returns_is_suspended()
    {
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
        var existing = Enumerable.Range(0, 20).Select(_ => Unique("ERP-ARRIVALS-OLD")).ToList();
        await RunAsync(new FixedSource([.. existing.Select(ErpRow)]));

        var arrivals = Enumerable.Range(0, 12).Select(_ => Unique("ERP-ARRIVALS-NEW")).ToList();
        var thisRun = new FixedSource([.. existing.Skip(6).Select(ErpRow), .. arrivals.Select(ErpRow)]);

        var preview = await PreviewAsync(thisRun);
        var run = await RunAsync(thisRun);

        preview.WouldSuspend.Should().Be(0, "6 of 20 active is above the limit of 5");
        preview.SuspensionsHeldBack.Should().NotBeNull();

        run.Suspended.Should().Be(
            0,
            "the first version counted this run's 12 new suppliers into the limit, which rose to 8 and let all 6 through "
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
            "a person reinstated it; a job nobody watches must not undo that on every run");
    }

    [Fact]
    public async Task A_supplier_that_left_while_suspended_and_was_reactivated_later_is_suspended_once()
    {
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
            "now a person has decided, and a job nobody watches must not undo that on every run");
    }

    [Fact]
    public async Task A_sync_suspension_is_not_lifted_by_a_later_document_approval()
    {
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
            + "invitable until the next run, with a 'you are reinstated' message, until the sync suspended it again");
    }

    [Fact]
    public async Task A_supplier_disabled_while_suspended_is_not_brought_back_by_a_document_and_a_person_is_overruled_once()
    {
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
    public async Task A_supplier_the_sync_suspends_is_not_reinstated_by_the_same_run_or_the_next()
    {
        var keep = Unique("ERP-SAME-RUN-KEEP");
        var id = Unique("ERP-SAME-RUN");
        await RunAsync(new FixedSource(ErpRow(keep), ErpRow(id)));
        await ExpireAnAwardCriticalDocumentAsync(id);
        await RunAsync(new FixedSource(ErpRow(keep)));
        await ApproveNewDocumentAsync(id, CommercialRegistration);
        await SetLifecycleAsync(id, supplier => supplier.Reactivate("The matter is closed."));

        await RunAsync(new FixedSource(ErpRow(keep), ErpRow(id) with { Disabled = true }));
        (await LifecycleOfAsync(id)).Should().Be(
            SupplierLifecycleState.Suspended,
            "the run suspended it for the disable and then asked whether to reinstate it; the reinstatement read only "
            + "saved audit rows, missed the suspension it had just made, and lifted it in the same save");

        await RunAsync(new FixedSource(ErpRow(keep), ErpRow(id)));
        (await LifecycleOfAsync(id)).Should().Be(
            SupplierLifecycleState.Suspended,
            "re-enabling does not reinstate a supplier the sync suspended; that is a person's decision");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_replacement_nobody_has_approved_does_not_bring_a_marked_supplier_back(bool rejected)
    {
        var keep = Unique("ERP-UNAPPROVED-KEEP");
        var id = Unique("ERP-UNAPPROVED");
        await RunAsync(new FixedSource(ErpRow(keep), ErpRow(id)));
        await ExpireAnAwardCriticalDocumentAsync(id);
        await RunAsync(new FixedSource(ErpRow(keep)));

        var replacement = await UploadNewDocumentAsync(id, CommercialRegistration);
        if (rejected) await RejectDocumentAsync(replacement);

        await RunAsync(new FixedSource(ErpRow(keep), ErpRow(id)));

        (await LifecycleOfAsync(id)).Should().Be(
            SupplierLifecycleState.Suspended,
            "the upload superseded the expired version, so the old check - no latest version expired - read a "
            + "replacement still waiting for review, or one a reviewer rejected, as fixed");
    }

    [Fact]
    public async Task An_empty_list_from_the_erp_marks_no_suspended_supplier_as_gone()
    {
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
            "the sync suspends once for a disable, and a person's reinstatement after that is not undone on every run");
    }

    [Fact]
    public async Task A_run_that_finds_nothing_new_leaves_the_supplier_exactly_as_it_was()
    {
        var id = Unique("ERP-UNCHANGED");
        await RunAsync(new FixedSource(ErpRow(id)));
        var before = await VersionOfAsync(id);

        await RunAsync(new FixedSource(ErpRow(id)));

        (await VersionOfAsync(id)).Should().Be(
            before,
            "the run is hourly; a version that moved on every run refused the save of anybody who opened the supplier "
            + "before the hour and saved after it, although nobody had changed anything");

        await RunAsync(new FixedSource(ErpRow(id) with { Phone = "+963911000000" }));

        (await VersionOfAsync(id)).RowVersion.Should().BeGreaterThan(
            before.RowVersion, "the control: a run that does bring something new is still an edit");
    }

    [Fact]
    public async Task A_supplier_the_erp_stops_approving_is_suspended_once_and_the_preview_says_so_first()
    {
        var id = Unique("ERP-UNAPPROVED");
        await RunAsync(new FixedSource(ErpRow(id)));
        var pending = new FixedSource(ErpRow(id) with { WorkflowState = "Pending Chief Accountant Approval" });

        var preview = await PreviewAsync(pending);
        var run = await RunAsync(pending);

        var forecast = preview.Rows.Single(r => r.ExternalId == id);
        var done = run.Rows.Single(r => r.ExternalId == id);
        forecast.Action.Should().Be(ErpImportAction.Suspend, "the preview must say what the hourly run is about to do");
        done.Outcome.Should().Be(
            ErpImportOutcome.Suspended, "counted as an update, the summary said nobody was suspended in a run that was");
        done.Notes.Should().ContainMatch("Not approved in the ERP ('Pending Chief Accountant Approval'); suspended*");
        forecast.Notes.Should().ContainMatch("Not approved in the ERP ('Pending Chief Accountant Approval'); suspended*");
        done.Notes.Should().NotContain(n => n.Contains("arrives suspended"), "it did not arrive; it was already here");
        (await LifecycleOfAsync(id)).Should().Be(SupplierLifecycleState.Suspended);

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var supplier = await db.Suppliers.SingleAsync(s => s.ExternalId == id);

            (await db.AuditLogs.AsNoTracking().SingleAsync(a =>
                    a.AggregateId == supplier.Id && a.Action == "supplier.suspended_not_approved_in_erp"))
                .Reason.Should().Be(
                    "Not approved in the ERP ('Pending Chief Accountant Approval').",
                    "filed under 'disabled', every report counting suppliers Seven Gates disabled would count this one");

            supplier.Reactivate("The ministry still works with them.");
            await db.SaveChangesAsync();
        }

        var later = await RunAsync(pending);

        (await LifecycleOfAsync(id)).Should().Be(
            SupplierLifecycleState.Active, "suspended once for this; a person's reinstatement after that stands");
        later.Rows.Single(r => r.ExternalId == id).Notes.Should().ContainMatch("*already dealt with*");
    }

    [Fact]
    public async Task A_new_supplier_waiting_for_erp_approval_comes_into_service_when_the_erp_approves_it()
    {
        var id = Unique("ERP-AWAITING");
        await RunAsync(new FixedSource(ErpRow(id) with { WorkflowState = "Pending Chief Accountant Approval" }));
        (await LifecycleOfAsync(id)).Should().Be(SupplierLifecycleState.Suspended);

        var approved = new FixedSource(ErpRow(id) with { WorkflowState = "Approved" });
        var preview = await PreviewAsync(approved);
        var run = await RunAsync(approved);

        preview.Rows.Single(r => r.ExternalId == id).Notes.Should().Contain(ErpImportAdmission.ReleasedNote);
        run.Rows.Single(r => r.ExternalId == id).Notes.Should().Contain(ErpImportAdmission.ReleasedNote);
        (await LifecycleOfAsync(id)).Should().Be(
            SupplierLifecycleState.Active,
            "hourly, most new suppliers are met while Seven Gates is still approving them; left suspended, every one "
            + "needed a person to notice and reinstate it");

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplierId = await db.Suppliers.Where(s => s.ExternalId == id).Select(s => s.Id).SingleAsync();
        (await db.AuditLogs.AsNoTracking().AnyAsync(a =>
                a.AggregateId == supplierId && a.Action == "supplier.reactivated_approved_in_erp" && a.ToState == "Active"))
            .Should().BeTrue("bringing a supplier into service is a lifecycle change and says who did it and why");
    }

    [Fact]
    public async Task A_supplier_whose_award_critical_document_expired_while_it_waited_is_not_released_by_the_approval()
    {
        var id = Unique("ERP-AWAITING-EXPIRED");
        await RunAsync(new FixedSource(ErpRow(id) with { WorkflowState = "Pending Chief Accountant Approval" }));

        await ExpireAnAwardCriticalDocumentAsync(id);
        var approved = await RunAsync(new FixedSource(ErpRow(id) with { WorkflowState = "Approved" }));

        (await LifecycleOfAsync(id)).Should().Be(
            SupplierLifecycleState.Suspended,
            "the expiry rule suspends only active suppliers and a document expires once; released, the supplier could "
            + "be invited with an expired commercial registration and nothing would ever look again");
        approved.Rows.Single(r => r.ExternalId == id).Notes.Should().Contain(ErpImportAdmission.ReleaseWaitsNote);

        var waiting = await VersionOfAsync(id);
        await RunAsync(new FixedSource(ErpRow(id) with { WorkflowState = "Approved" }));
        (await VersionOfAsync(id)).Should().Be(
            waiting,
            "waiting changes nothing; a version moved every hour made the reviewer's approval of the very renewal it "
            + "waits for fail as a conflict");

        await ApproveNewDocumentAsync(id, CommercialRegistration);
        await RunAsync(new FixedSource(ErpRow(id) with { WorkflowState = "Approved" }));

        (await LifecycleOfAsync(id)).Should().Be(
            SupplierLifecycleState.Active,
            "with the approval and the renewal both in, the supplier comes back; the first fix withdrew the release at "
            + "expiry and left it for a person to notice");
    }

    [Fact]
    public async Task A_person_who_keeps_a_waiting_supplier_suspended_is_not_overruled_by_the_approval()
    {
        var id = Unique("ERP-AWAITING-KEPT");
        await RunAsync(new FixedSource(ErpRow(id) with { WorkflowState = "Pending Chief Accountant Approval" }));
        await SetLifecycleAsync(id, supplier => supplier.Suspend("Sanctions check pending."));

        await RunAsync(new FixedSource(ErpRow(id) with { WorkflowState = "Approved" }));

        (await LifecycleOfAsync(id)).Should().Be(
            SupplierLifecycleState.Suspended,
            "suspending it again is how a person makes the suspension theirs; the ERP approving it says nothing about "
            + "their reason");
    }

    [Fact]
    public async Task A_held_back_mass_disable_leaves_a_waiting_supplier_able_to_come_back_when_approved()
    {
        var ids = Enumerable.Range(1, 8).Select(i => Unique($"ERP-MASS-{i}")).ToList();
        var waiting = Unique("ERP-MASS-WAITING");
        await RunAsync(new FixedSource(
            [.. ids.Select(ErpRow), ErpRow(waiting) with { WorkflowState = "Pending Chief Accountant Approval" }]));

        var held = await RunAsync(new FixedSource(
            [.. ids.Select(id => ErpRow(id) with { Disabled = true }), ErpRow(waiting) with { Disabled = true }]));
        held.SuspensionsHeldBack.Should().NotBeNull();

        await RunAsync(new FixedSource([.. ids.Select(ErpRow), ErpRow(waiting) with { WorkflowState = "Approved" }]));

        (await LifecycleOfAsync(waiting)).Should().Be(
            SupplierLifecycleState.Active,
            "the disable came from a read the run did not believe; turning its pending hold into a disable would have "
            + "left it for a person to reinstate by hand once the ERP was put right");
    }

    [Fact]
    public async Task A_held_back_run_does_not_reinstate_a_supplier_the_erp_is_still_turning_away()
    {
        var ids = Enumerable.Range(1, 8).Select(i => Unique($"ERP-HELD-REINSTATE-{i}")).ToList();
        var target = Unique("ERP-HELD-TARGET");
        await RunAsync(new FixedSource([.. ids.Select(ErpRow), ErpRow(target)]));

        await ExpireAnAwardCriticalDocumentAsync(target);
        (await LifecycleOfAsync(target)).Should().Be(SupplierLifecycleState.Suspended, "the control: expiry suspended it");

        await RunAsync(new FixedSource([.. ids.Select(ErpRow)]));
        (await SyncStatusOfAsync(target)).Should().Be(SupplierSyncStatus.MarkedRemovedFromErp);
        await ApproveNewDocumentAsync(target, CommercialRegistration);
        (await LifecycleOfAsync(target)).Should().Be(
            SupplierLifecycleState.Suspended, "the control: the renewal waits while the ERP no longer offers it");

        var held = await RunAsync(new FixedSource(
            [.. ids.Select(id => ErpRow(id) with { Disabled = true }), ErpRow(target) with { Disabled = true }]));

        held.SuspensionsHeldBack.Should().NotBeNull();
        (await LifecycleOfAsync(target)).Should().Be(
            SupplierLifecycleState.Suspended,
            "the ERP returned it disabled; a held run must not clear its gone mark and then reinstate it on the renewal");
        (await SyncStatusOfAsync(target)).Should().Be(SupplierSyncStatus.MarkedRemovedFromErp, "the mark stands until a believed run");
    }

    [Fact]
    public async Task A_waiting_supplier_the_erp_disables_loses_its_release_and_the_trail_says_so()
    {
        var id = Unique("ERP-AWAITING-DISABLED");
        await RunAsync(new FixedSource(ErpRow(id) with { WorkflowState = "Pending Chief Accountant Approval" }));
        var disabled = await RunAsync(new FixedSource(ErpRow(id) with { Disabled = true }));
        await RunAsync(new FixedSource(ErpRow(id) with { WorkflowState = "Approved" }));

        (await LifecycleOfAsync(id)).Should().Be(
            SupplierLifecycleState.Suspended, "the ERP may lift only its own wait; a disable is lifted by a person");
        disabled.Rows.Single(r => r.ExternalId == id).Notes.Should().ContainMatch("*no longer bring it back*");

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplierId = await db.Suppliers.Where(s => s.ExternalId == id).Select(s => s.Id).SingleAsync();
        (await db.AuditLogs.AsNoTracking().AnyAsync(a => a.AggregateId == supplierId && a.Action == "supplier.erp_release_withdrawn"))
            .Should().BeTrue("otherwise nobody can tell later why the approval did not bring it back");
    }

    [Fact]
    public async Task Keeping_a_waiting_supplier_suspended_is_audited_as_that()
    {
        var id = Unique("ERP-KEEP-AUDIT");
        await RunAsync(new FixedSource(ErpRow(id) with { WorkflowState = "Pending Chief Accountant Approval" }));

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var referenceCode = await db.Suppliers.Where(s => s.ExternalId == id).Select(s => s.ReferenceCode).SingleAsync();
        var result = await new SupplierLifecycleHandler(
                db,
                scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(),
                new TestScope(Guid.CreateVersion7()),
                scope.ServiceProvider.GetRequiredService<IAuditLogger>())
            .SuspendAsync(new SupplierLifecycleCommand(referenceCode, "Sanctions check pending."), CancellationToken.None);

        result.Should().BeOfType<SupplierLifecycleResult.Success>();
        var supplierId = await db.Suppliers.Where(s => s.ExternalId == id).Select(s => s.Id).SingleAsync();
        (await db.AuditLogs.AsNoTracking().SingleAsync(a => a.AggregateId == supplierId && a.Action == "supplier_kept_suspended"))
            .ToState.Should().Be("Suspended", "a person's suspension must still read as the latest one");
    }

    [Fact]
    public async Task Most_suppliers_turned_away_at_once_are_held_back_and_a_few_are_suspended()
    {
        var ids = Enumerable.Range(1, 8).Select(i => Unique($"ERP-TURNED-{i}")).ToList();
        await RunAsync(new FixedSource([.. ids.Select(ErpRow)]));

        ErpSupplier Pending(string id) => ErpRow(id) with { WorkflowState = "Pending Chief Accountant Approval" };

        var many = new FixedSource([.. ids.Take(6).Select(Pending), .. ids.Skip(6).Select(ErpRow)]);
        var preview = await PreviewAsync(many);
        var held = await RunAsync(many);

        held.SuspensionsHeldBack.Should().Contain("6 it turned away");
        preview.SuspensionsHeldBack.Should().Be(held.SuspensionsHeldBack, "the preview must warn of the same hold");
        held.Suspended.Should().Be(0);
        foreach (var id in ids)
        {
            (await LifecycleOfAsync(id)).Should().Be(
                SupplierLifecycleState.Active,
                "six of eight at once is a change on Seven Gates' side; suspending them all in a run nobody watches "
                + "would leave a person to reinstate every one by hand");
        }

        var few = await RunAsync(new FixedSource([.. ids.Take(5).Select(Pending), .. ids.Skip(5).Select(ErpRow)]));

        few.SuspensionsHeldBack.Should().BeNull();
        few.Suspended.Should().Be(5, "the control: five at once is ordinary, and they are suspended");
    }

    [Fact]
    public async Task A_supplier_the_import_creates_is_on_the_audit_trail_once_naming_whoever_ran_it()
    {
        var id = Unique("ERP-CREATED-AUDITED");
        var person = Guid.CreateVersion7();
        await RunAsync(new FixedSource(ErpRow(id)), userId: person);
        await RunAsync(new FixedSource(ErpRow(id)), userId: person);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplier = await db.Suppliers.AsNoTracking().SingleAsync(s => s.ExternalId == id);
        var trail = await db.AuditLogs.AsNoTracking().Where(a => a.AggregateId == supplier.Id).ToListAsync();

        var imported = trail.Should().ContainSingle(
            a => a.Action == "supplier.imported_from_erp",
            "the trail must say where the supplier came from, and a second run updates it rather than bringing it again")
            .Subject;
        imported.ActorUserId.Should().Be(person, "bringing a company into the registry was somebody's decision");
        imported.ToState.Should().Be("Approved");
        imported.ReferenceCode.Should().Be(supplier.ReferenceCode);
        imported.Reason.Should().Contain(id, "the ERP's own identifier is how anybody finds the record on the other side");
        trail.Should().NotContain(
            a => a.ToState == "Suspended", "the control: a supplier the ERP lets be used arrives in service");
    }

    [Fact]
    public async Task A_supplier_that_arrives_suspended_has_the_reason_on_the_audit_trail()
    {
        var disabled = Unique("ERP-ARRIVES-DISABLED");
        var pending = Unique("ERP-ARRIVES-PENDING");
        await using (var run = fixture.Services.CreateAsyncScope())
        {
            await Handler(run, new FixedSource(
                    ErpRow(disabled) with { Disabled = true },
                    ErpRow(pending) with { WorkflowState = "Pending Chief Accountant Approval" }))
                .HandleAsync(ErpImportTrigger.Scheduled, CancellationToken.None);
        }

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var disabledTrail = await TrailOfAsync(db, disabled);
        var pendingTrail = await TrailOfAsync(db, pending);

        var disabledSuspension = disabledTrail.Should().ContainSingle(a => a.ToState == "Suspended").Subject;
        disabledSuspension.Action.Should().Be("supplier.suspended_disabled_in_erp");
        disabledSuspension.Reason.Should().Be("Disabled in the ERP; it arrived suspended.");
        disabledSuspension.FromState.Should().BeNull("it was never in service here");
        disabledSuspension.ActorUserId.Should().BeNull();
        disabledSuspension.ActorLabel.Should().Be(
            "system", "the hourly run has nobody to name, and says so rather than leaving the actor blank");
        disabledSuspension.OccurredAt.Should().BeOnOrAfter(
            disabledTrail.Single(a => a.Action == "supplier.imported_from_erp").OccurredAt,
            "the trail reads in the order it happened: the supplier arrives, then is out of service");

        var pendingSuspension = pendingTrail.Should().ContainSingle(a => a.ToState == "Suspended").Subject;
        pendingSuspension.Action.Should().Be(
            "supplier.suspended_not_approved_in_erp",
            "filed under 'disabled', every report counting suppliers Seven Gates disabled would count this one");
        pendingSuspension.Reason.Should().Be(
            "Not approved in the ERP ('Pending Chief Accountant Approval'); it arrived suspended.");
    }

    [Fact]
    public async Task A_supplier_whose_arrival_cannot_be_recorded_is_not_created()
    {
        var id = Unique("ERP-UNRECORDED");
        ErpImportRunReport report;
        await using (var run = fixture.Services.CreateAsyncScope())
        {
            report = await new RunErpImportHandler(
                    new FixedSource(ErpRow(id)),
                    run.ServiceProvider.GetRequiredService<AppDbContext>(),
                    run.ServiceProvider.GetRequiredService<UserManager<AppUser>>(),
                    Options.Create(new ErpImportOptions { InitialPassword = Password }),
                    new RefusingAudit(run.ServiceProvider.GetRequiredService<IAuditLogger>(), "supplier.imported_from_erp"),
                    new TestScope(),
                    NullLogger<RunErpImportHandler>.Instance)
                .HandleAsync(ErpImportTrigger.Manual, CancellationToken.None);
        }

        report.Rows.Single(r => r.ExternalId == id).Outcome.Should().Be(ErpImportOutcome.Failed);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        (await db.Suppliers.AnyAsync(s => s.ExternalId == id)).Should().BeFalse(
            "the rows are written in the supplier's own transaction; written after it committed, a failure left a "
            + "supplier in the registry with nothing on the trail - the defect these rows exist to end");
        (await users.FindByEmailAsync($"{id.ToLowerInvariant()}@sgtest.example")).Should().BeNull(
            "its account goes with it, or a login would exist for a company the portal does not hold");
    }

    [Fact]
    public async Task A_manual_run_names_the_person_who_ran_it_on_every_suspension()
    {
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
        var person = Guid.CreateVersion7();

        await using (var holder = fixture.Services.CreateAsyncScope())
        {
            var holderDb = holder.ServiceProvider.GetRequiredService<AppDbContext>();
            await holderDb.Database.OpenConnectionAsync();

            try
            {
                (await holderDb.Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock({7_346_815_201_001L}) AS \"Value\"")
                    .SingleAsync()).Should().BeTrue("the test must actually hold the lock for this to prove anything");

                var act = () => RunAsync(new FixedSource(ErpRow(Unique("ERP-BUSY"))), userId: person);

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

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.AuditLogs.AsNoTracking().AnyAsync(a => a.ActorUserId == person)).Should().BeFalse(
            "a run refused for the lock never started, and a trail reading 'started, failed' would send somebody "
            + "looking for a failure that did not happen");
    }

    // A RUN IS OPENED AND CLOSED ON THE TRAIL BY WHOEVER RAN IT: ErpImportRun before the first supplier, and
    // ErpImportCompleted with every count once it has finished. Each count is a different number here, so a count
    // written under another's name fails.
    [Fact]
    public async Task A_manual_run_is_opened_and_closed_on_the_trail_by_the_person_who_ran_it()
    {
        var kept = new[] { Unique("ERP-CLOSED-KEPT"), Unique("ERP-CLOSED-KEPT") };
        var gone = new[] { Unique("ERP-CLOSED-GONE"), Unique("ERP-CLOSED-GONE"), Unique("ERP-CLOSED-GONE") };
        await RunAsync(new FixedSource([.. kept.Select(ErpRow), .. gone.Select(ErpRow)]));

        var person = Guid.CreateVersion7();
        var report = await RunAsync(
            new FixedSource(
            [
                .. kept.Select(ErpRow),
                ErpRow(Unique("ERP-CLOSED-NEW")),
                .. Enumerable.Range(0, 4).Select(_ => ErpRow(Unique("ERP-CLOSED-PUSHED")) with { CreatedByPortal = true }),
            ]),
            userId: person);

        (report.Created, report.Updated, report.Suspended, report.Refused, report.Failed)
            .Should().Be((1, 2, 3, 4, 0), "the control: the run did what the counts below are meant to say");

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var trail = await db.AuditLogs.AsNoTracking()
            .Where(a => a.AggregateId == Guid.Empty && a.ActorUserId == person)
            .OrderBy(a => a.OccurredAt).ThenBy(a => a.Id)
            .ToListAsync();

        trail.Select(a => a.Action).Should().Equal(
            ["ErpImportRun", "ErpImportCompleted"],
            "a run whose start is on the trail and whose end is not reads like one still running, or one that died");
        trail.Should().OnlyContain(a =>
            a.AggregateType == "Supplier" && a.ActorKind == AuditActorKind.User && a.ActorLabel == null);

        var completed = trail[1];
        completed.ToState.Should().Be("Succeeded");
        completed.Reason.Should().Be("7 in the ERP: 1 created, 2 updated, 3 suspended, 4 refused, 0 failed.");

        var changes = JsonDocument.Parse(completed.Changes!).RootElement;
        changes.GetProperty("trigger").GetString().Should().Be("Manual");
        changes.GetProperty("erpSuppliers").GetInt32().Should().Be(7);
        changes.GetProperty("created").GetInt32().Should().Be(1);
        changes.GetProperty("updated").GetInt32().Should().Be(2);
        changes.GetProperty("suspended").GetInt32().Should().Be(3);
        changes.GetProperty("refused").GetInt32().Should().Be(4);
        changes.GetProperty("failed").GetInt32().Should().Be(0);
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
                "a scheduled failure nobody records looks exactly like a run with nothing to do");
            row.LastSyncSummary.Should().Contain("InitialPassword");
        }
    }

    // A RUN THAT THROWS IS CLOSED ON THE TRAIL AS FAILED, under the actor that opened it and with what went wrong. The
    // scheduled run is the one tested because it is the one nobody watches, and its scope is given a person on purpose:
    // the hourly job has nobody to name, so its rows say "system" whoever the scope might hold.
    [Fact]
    public async Task A_scheduled_run_that_fails_is_closed_on_the_trail_as_failed_by_the_system()
    {
        Guid correlation;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var failing = new RunErpImportHandler(
                new FixedSource(),
                scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(),
                Options.Create(new ErpImportOptions { InitialPassword = null }),
                scope.ServiceProvider.GetRequiredService<IAuditLogger>(),
                new TestScope(Guid.CreateVersion7()),
                NullLogger<RunErpImportHandler>.Instance);

            var act = () => failing.HandleAsync(ErpImportTrigger.Scheduled, CancellationToken.None);
            await act.Should().ThrowAsync<ErpImportNotConfiguredException>(
                "the closing row is a record of the failure, not a replacement for it");

            correlation = scope.ServiceProvider.GetRequiredService<IAuditContext>().CorrelationId;
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var trail = await db.AuditLogs.AsNoTracking()
                .Where(a => a.AggregateId == Guid.Empty && a.CorrelationId == correlation)
                .OrderBy(a => a.OccurredAt).ThenBy(a => a.Id)
                .ToListAsync();

            trail.Select(a => a.Action).Should().Equal(["ErpImportRun", "ErpImportFailed"]);
            trail.Should().OnlyContain(
                a => a.ActorUserId == null && a.ActorLabel == "system" && a.ActorKind == AuditActorKind.System,
                "an hourly run's rows name the system rather than nobody, and never a person who did not press anything");

            var failed = trail[1];
            failed.ToState.Should().Be("Failed");
            failed.Reason.Should().Be("The import failed: No initial password is configured: set ErpImport:InitialPassword.");

            var changes = JsonDocument.Parse(failed.Changes!).RootElement;
            changes.GetProperty("trigger").GetString().Should().Be("Scheduled");
            changes.GetProperty("failure").GetString().Should().Be("ErpImportNotConfiguredException");
            changes.GetProperty("message").GetString().Should().Be(
                "No initial password is configured: set ErpImport:InitialPassword.");
        }
    }

    // AN INTERRUPTED RUN IS CLOSED AS INTERRUPTED, not as a failure of some named setting: a run cancelled part-way, by
    // a shutdown or a caller going away, is somebody else's decision rather than something to fix.
    [Fact]
    public async Task A_run_that_is_interrupted_is_closed_on_the_trail_as_interrupted()
    {
        var person = Guid.CreateVersion7();

        var act = () => RunAsync(new InterruptedSource(), userId: person);
        await act.Should().ThrowAsync<OperationCanceledException>();

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var trail = await db.AuditLogs.AsNoTracking()
            .Where(a => a.AggregateId == Guid.Empty && a.ActorUserId == person)
            .OrderBy(a => a.OccurredAt).ThenBy(a => a.Id)
            .ToListAsync();

        trail.Select(a => a.Action).Should().Equal(["ErpImportRun", "ErpImportFailed"]);

        var failed = trail[1];
        failed.Reason.Should().Be("The import was interrupted before it finished.");

        var changes = JsonDocument.Parse(failed.Changes!).RootElement;
        changes.GetProperty("trigger").GetString().Should().Be("Manual");
        changes.GetProperty("failure").GetString().Should().Be("Interrupted");
    }

    // RECORDING A FAILURE NEVER HIDES IT. The closing row is written in the same guarded save as the connection's
    // outcome, so an audit store that refuses it leaves the import's own exception to reach whoever ran it.
    [Fact]
    public async Task A_failed_run_whose_closing_row_cannot_be_written_still_reports_its_own_failure()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var failing = new RunErpImportHandler(
            new FixedSource(),
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(),
            Options.Create(new ErpImportOptions { InitialPassword = null }),
            new RefusingAudit(scope.ServiceProvider.GetRequiredService<IAuditLogger>(), "ErpImportFailed"),
            new TestScope(Guid.CreateVersion7()),
            NullLogger<RunErpImportHandler>.Instance);

        var act = () => failing.HandleAsync(ErpImportTrigger.Manual, CancellationToken.None);

        await act.Should().ThrowAsync<ErpImportNotConfiguredException>(
            "an administrator told 'the audit store refused the row' would go looking in the wrong place");
    }

    [Fact]
    public async Task The_scheduled_job_does_nothing_when_no_erp_is_configured()
    {
        var calls = 0;
        var import = new CountingImport(() => calls++);

        var job = new ErpSupplierSyncJob(
            new FixedConnection(null), import, new RecordingJobClient(), NullLogger<ErpSupplierSyncJob>.Instance);
        await job.RunAsync();

        var disabled = new ErpSupplierSyncJob(
            new FixedConnection(new ErpConnection("http://x", "k", "s", IsEnabled: false, ErpConnectionSource.Database)),
            import,
            new RecordingJobClient(),
            NullLogger<ErpSupplierSyncJob>.Instance);
        await disabled.RunAsync();

        calls.Should().Be(
            0,
            "a deployment that is not integrated yet is not failing every hour, and recording that it is would "
            + "teach people to ignore the failure that eventually matters");
    }

    // THE LOCK TAKEN AT THE HOUR IS TRIED ONCE MORE, NOT SKIPPED FOR THE HOUR. A supplier push holds it too, and a push
    // does none of the import's work, so the first version, which stepped aside until the next hour, held back an
    // hour's releases and suspensions whenever an approval landed at the turn of the hour. The second try steps aside
    // and schedules nothing, so a lock held for long never queues imports behind it.
    [Fact]
    public async Task The_scheduled_job_tries_once_more_a_couple_of_minutes_after_finding_the_lock_taken()
    {
        var jobs = new RecordingJobClient();
        var job = new ErpSupplierSyncJob(
            new FixedConnection(new ErpConnection("http://x", "k", "s", IsEnabled: true, ErpConnectionSource.Database)),
            new CountingImport(() => throw new ErpImportBusyException()),
            jobs,
            NullLogger<ErpSupplierSyncJob>.Instance);

        var act = () => job.RunAsync();

        await act.Should().NotThrowAsync("a lock somebody else holds is not a failure of this run");
        var retry = jobs.Created.Should().ContainSingle(
            "the lock may be a supplier push, which does none of this hour's work").Subject;
        retry.Type.Should().Be(typeof(ErpSupplierSyncJob));
        retry.Method.Should().Be(nameof(ErpSupplierSyncJob.RunAgainAsync));
        retry.State.Should().BeOfType<Hangfire.States.ScheduledState>()
            .Which.EnqueueAt.Should().BeCloseTo(
                DateTime.UtcNow + ErpSupplierSyncJob.RetryAfterBusy, TimeSpan.FromSeconds(30));
        ErpSupplierSyncJob.RetryAfterBusy.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task The_second_try_steps_aside_when_the_lock_is_still_taken_and_schedules_nothing_more()
    {
        var jobs = new RecordingJobClient();
        var job = new ErpSupplierSyncJob(
            new FixedConnection(new ErpConnection("http://x", "k", "s", IsEnabled: true, ErpConnectionSource.Database)),
            new CountingImport(() => throw new ErpImportBusyException()),
            jobs,
            NullLogger<ErpSupplierSyncJob>.Instance);

        var act = () => job.RunAgainAsync();

        await act.Should().NotThrowAsync();
        jobs.Created.Should().BeEmpty("one more try is all; the next hour runs as usual");
    }

    [Fact]
    public async Task The_scheduled_job_schedules_nothing_when_the_import_runs()
    {
        var calls = 0;
        var jobs = new RecordingJobClient();
        var job = new ErpSupplierSyncJob(
            new FixedConnection(new ErpConnection("http://x", "k", "s", IsEnabled: true, ErpConnectionSource.Database)),
            new CountingImport(() => calls++),
            jobs,
            NullLogger<ErpSupplierSyncJob>.Instance);

        await job.RunAsync();

        calls.Should().Be(1);
        jobs.Created.Should().BeEmpty();
    }

    private sealed class RecordingJobClient : Hangfire.IBackgroundJobClient
    {
        public List<(Type Type, string Method, Hangfire.States.IState State)> Created { get; } = [];

        public string Create(Hangfire.Common.Job job, Hangfire.States.IState state)
        {
            Created.Add((job.Type, job.Method.Name, state));
            return Guid.NewGuid().ToString();
        }

        public bool ChangeState(string jobId, Hangfire.States.IState state, string expectedState) => true;
    }

    private sealed class CountingImport(Action onRun) : IRunErpImportHandler
    {
        public Task<ErpImportRunReport> HandleAsync(ErpImportTrigger trigger, CancellationToken ct)
        {
            onRun();
            return Task.FromResult(new ErpImportRunReport(0, 0, 0, 0, 0, []));
        }
    }

    private sealed class RefusingAudit(IAuditLogger inner, string refusedAction) : IAuditLogger
    {
        public Task LogAsync(
            string aggregateType,
            Guid aggregateId,
            string action,
            Guid? actorUserId = null,
            string? actorLabel = null,
            string? fromState = null,
            string? toState = null,
            string? reason = null,
            string? referenceCode = null,
            string? changes = null,
            CancellationToken ct = default) =>
            action == refusedAction
                ? throw new InvalidOperationException("The audit store refused the row.")
                : inner.LogAsync(
                    aggregateType, aggregateId, action, actorUserId, actorLabel, fromState, toState, reason,
                    referenceCode, changes, ct);
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
