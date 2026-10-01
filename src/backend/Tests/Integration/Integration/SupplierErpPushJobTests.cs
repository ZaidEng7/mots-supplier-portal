// Creating in the ERP a supplier approved here, against a real database and a recording fake of the ERP.
//
// THE FAKE IS THE ERP'S BEHAVIOUR, NOT A SCRIPT. It keeps what it was sent - suppliers, addresses, contacts, users,
// portal users - and answers the reads from that, so a push that stopped part-way meets the records it made before,
// as it would on the real ERP. Each test tells it which call to refuse, and whether the refusal comes before the
// record is made or after, which is the difference between a create that failed and one whose answer was lost. It
// can also lag, answering its searches as though a record it made were not committed yet, which is how a lost answer
// is followed by a look that finds nothing. Every call is recorded in order, so the colleague's order and "no second
// create" are both read from one list.
//
// THE SUPPLIER IS APPROVED THROUGH THE REVIEWER'S HANDLER, with a recording job client in place of the scheduler, so
// the push request is the one approval really makes and the enqueued push is asserted rather than assumed. The job is
// then run directly, as the enqueued run would be. The connection is handed to it, so the shared connection row stays
// off: a push the scheduler runs from some other class's approval finds nothing switched on and does nothing.
//
// EVERY TEST STARTS WITH NO OTHER PUSH DUE AND NO IMPORTED SUPPLIER IN SERVICE. The job pushes every due supplier in
// the table, and other classes approve suppliers, so their pushes are moved far into the future first. The round trip
// runs the real import, which judges the whole table, so imported suppliers earlier tests left active are suspended
// first, as ErpSupplierSyncTests does before each of its tests.
//
// THE LOCK IS TESTED BY HOLDING IT, both ways: a push while a test holds it, and an import started from inside the
// push's Supplier create, which must be refused. A test that raced two runs would pass whenever they happened not to
// collide.
//
// Nothing here calls a real ERP.

namespace MotsSupplierPortal.Tests.Integration.Integration;

using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Integration;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Integration.Erp;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Suppliers;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class SupplierErpPushJobTests(PostgresApiFixture fixture) : IAsyncLifetime
{
    private const string Group = "Local Suppliers - SYP";
    private const string ImportPassword = "Wattle-Harbour-Quince-72";
    private const long ErpLockKey = 7_346_815_201_001;

    private const string SupplierPost = "POST Supplier";
    private const string AddressPost = "POST Address";
    private const string ContactPost = "POST Contact";
    private const string UserPost = "POST User";
    private const string PortalUsersPut = "PUT Supplier portal_users";
    private const string ContactUserPut = "PUT Contact user";
    private const string SupplierFieldsRead = "GET fields Supplier";
    private const string TaxIdSearch = "GET Supplier by tax_id";
    private const string PortalCreatesSearch = "GET Supplier created by the portal";

    private static readonly DateTimeOffset Parked = new(2999, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly ErpConnection On = new(
        "http://erp.push.test", "key", "secret", IsEnabled: true, ErpConnectionSource.Database,
        CreateSuppliersInErp: true, DefaultSupplierGroup: Group);

    public async Task InitializeAsync()
    {
        await IntegrationConnectionTests.ResetAsync(fixture);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Suppliers
            .Where(s => s.ErpPushStatus == SupplierErpPushStatus.Requested || s.ErpPushStatus == SupplierErpPushStatus.Linked)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.ErpPushNextAttemptAt, Parked));

        foreach (var supplier in await db.Suppliers
                     .Where(s => s.ExternalId != null && s.LifecycleState == SupplierLifecycleState.Active)
                     .ToListAsync())
        {
            supplier.Suspend("Isolating an ERP push test.");
        }

        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => IntegrationConnectionTests.ResetAsync(fixture);

    private sealed record Approved(
        Guid Id, string ReferenceCode, string Name, string Email, string? TaxId, RecordingJobClient Jobs);

    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..(prefix.Length + 13)];

    private async Task<Approved> ApprovedAsync(
        string? taxId = null, string country = "Syria", SupplierLegalType legalType = SupplierLegalType.Company)
    {
        var name = Unique("Push Trading");
        var email = $"push-{Guid.NewGuid():N}@push.example";
        var referenceCode = $"SUP-P-{Guid.NewGuid():N}"[..24];

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var supplier = Supplier.Register(
                referenceCode, "شركة الدفع", name, $"RC-{Guid.NewGuid():N}"[..16], "Rana Haddad", email,
                "+963944000111");
            supplier.MarkEmailVerified();
            supplier.UpdateCoreProfile("Office supplies.", null, "SME", "SYP");
            supplier.UpdateLegalInfo(
                "شركة الدفع", name, supplier.LegalInfo!.RegistrationNumber, taxId, legalType, null,
                isComplianceCritical: true);
            supplier.AddAddress(AddressKind.HeadOffice, "12 Baghdad Street", null, "Damascus", "DIM", country, null, null, null);
            supplier.LinkCategory("catering", isComplianceCritical: true);
            supplier.AcceptTerms(Supplier.CurrentTermsVersion);
            supplier.Submit([]);
            supplier.PickUpForReview();

            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();
        }

        var id = await IdOfAsync(referenceCode);
        await ApproveEveryRequiredDocumentAsync(id);

        var jobs = new RecordingJobClient();
        await ApproveAsync(referenceCode, jobs);

        return new Approved(id, referenceCode, name, email, taxId, jobs);
    }

    private async Task ApproveAsync(string referenceCode, RecordingJobClient jobs)
    {
        await using var scope = fixture.Services.CreateAsyncScope();

        var result = await new ApproveApplicationHandler(
                scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                new TestScope(Guid.CreateVersion7()),
                scope.ServiceProvider.GetRequiredService<IAuditLogger>(),
                jobs)
            .HandleAsync(referenceCode, CancellationToken.None);

        result.Should().BeOfType<ReviewDecisionResult.Success>();
    }

    private async Task ApproveEveryRequiredDocumentAsync(Guid supplierId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var blocking = await DocumentCompletenessEvaluator
            .GetBlockingRequiredDocumentTypeCodesAsync(db, supplierId, CancellationToken.None);
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);

        foreach (var typeId in await db.DocumentTypes.Where(t => blocking.Contains(t.Code)).Select(t => t.Id).ToListAsync())
        {
            var document = SupplierDocument.CreatePendingScan(
                $"DOC-2026-{Guid.NewGuid().ToString("N")[..6]}", supplierId, typeId, 1, "quarantine/key",
                $"push-{Guid.NewGuid():N}.pdf", "application/pdf", 1024, Guid.CreateVersion7(),
                issueDate: null, expiryDate: null, expiryTracked: false, today: today);
            document.MarkScanClean("clean/key");
            document.Approve(Guid.CreateVersion7());
            db.SupplierDocuments.Add(document);
        }

        await db.SaveChangesAsync();
    }

    private async Task<Guid> IdOfAsync(string referenceCode)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Suppliers.Where(s => s.ReferenceCode == referenceCode).Select(s => s.Id).SingleAsync();
    }

    private async Task<Supplier> ReadAsync(Guid supplierId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Suppliers.AsNoTracking().SingleAsync(s => s.Id == supplierId);
    }

    private async Task<List<MotsSupplierPortal.Domain.Audit.AuditLog>> PushTrailAsync(Guid supplierId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditLogs.AsNoTracking()
            .Where(a => a.AggregateId == supplierId && a.Action.StartsWith("supplier.erp_push"))
            .OrderBy(a => a.OccurredAt)
            .ToListAsync();
    }

    private async Task MakeDueAsync(Guid supplierId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Suppliers.Where(s => s.Id == supplierId)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.ErpPushNextAttemptAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
    }

    private async Task PushAsync(
        FakeErp erp, Guid? only = null, ErpConnection? connection = null, Func<IAuditLogger, IAuditLogger>? audit = null)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var auditLogger = scope.ServiceProvider.GetRequiredService<IAuditLogger>();

        var job = new SupplierErpPushJob(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            new FixedConnection(connection),
            erp,
            audit?.Invoke(auditLogger) ?? auditLogger,
            NullLogger<SupplierErpPushJob>.Instance);

        if (only is { } supplierId)
        {
            await job.PushAsync(supplierId);
        }
        else
        {
            await job.RunAsync();
        }
    }

    private Task PushOneAsync(FakeErp erp, Guid supplierId, Func<IAuditLogger, IAuditLogger>? audit = null) =>
        PushAsync(erp, supplierId, On, audit);

    private async Task<ErpImportRunReport> ImportAsync(params ErpSupplier[] suppliers)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        return await ImportHandler(scope, suppliers).HandleAsync(ErpImportTrigger.Manual, CancellationToken.None);
    }

    private static RunErpImportHandler ImportHandler(AsyncServiceScope scope, params ErpSupplier[] suppliers) =>
        new(
            new FixedSource(suppliers),
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(),
            Options.Create(new ErpImportOptions { InitialPassword = ImportPassword }),
            scope.ServiceProvider.GetRequiredService<IAuditLogger>(),
            new TestScope(Guid.CreateVersion7()),
            NullLogger<RunErpImportHandler>.Instance);

    private static ErpRequestException Refusal(HttpStatusCode status, HttpMethod method, string? excType = null, string? erpMessage = null) =>
        new(status, excType, $"The ERP refused it with {(int)status} {status}" + (erpMessage is null ? "." : $": {erpMessage}"),
            erpMessage, method);

    private static void AssertUntouched(Supplier supplier)
    {
        supplier.ErpPushStatus.Should().Be(SupplierErpPushStatus.Requested);
        supplier.ExternalId.Should().BeNull();
        supplier.ErpPushAttempts.Should().Be(0);
        supplier.ErpPushLastError.Should().BeNull();
        supplier.ErpPushNextAttemptAt.Should().Be(supplier.ErpPushRequestedAt, "the first attempt is still due at once");
    }

    [Fact]
    public async Task An_approval_enqueues_the_push_and_the_push_creates_the_supplier_in_the_colleagues_order()
    {
        var approved = await ApprovedAsync();

        var push = approved.Jobs.Enqueued.Should().ContainSingle(e => e.Type == typeof(SupplierErpPushJob)).Subject;
        push.Method.Should().Be(nameof(SupplierErpPushJob.PushAsync));
        push.Args[0].Should().Be(approved.Id, "the run straight after approval pushes the supplier just approved");

        var erp = new FakeErp();
        await PushOneAsync(erp, approved.Id);

        erp.Writes.Should().Equal(SupplierPost, AddressPost, ContactPost, UserPost, PortalUsersPut, ContactUserPut);

        var created = erp.Suppliers.Should().ContainSingle().Subject;
        created.Body["supplier_name"]!.GetValue<string>().Should().Be(approved.Name);
        created.Body["supplier_group"]!.GetValue<string>().Should().Be(Group);
        erp.Addresses.Should().ContainSingle().Which.Supplier.Should().Be(created.Name);
        erp.Contacts.Should().ContainSingle().Which.Should().Match<FakeContact>(c =>
            c.Supplier == created.Name && c.Email == approved.Email && c.User == approved.Email);
        erp.PortalUsers[created.Name].Should().Equal(approved.Email);

        var supplier = await ReadAsync(approved.Id);
        supplier.ExternalId.Should().Be(created.Name, "the import matches the supplier by the ERP's name from now on");
        supplier.ErpPushStatus.Should().Be(SupplierErpPushStatus.Created);
        supplier.ErpPushStartedAt.Should().BeNull();
        supplier.ErpPushNextAttemptAt.Should().BeNull();
        supplier.SyncStatus.Should().Be(SupplierSyncStatus.Pending, "the push never writes the sync's own state");

        var trail = await PushTrailAsync(approved.Id);
        trail.Select(a => a.Action).Should().Equal("supplier.erp_push_created", "supplier.erp_push_completed");
        trail.Should().OnlyContain(a => a.ActorLabel == "system" && a.ActorUserId == null);
        trail[0].Reason.Should().Contain(created.Name);

        var before = erp.Calls.Count;
        await PushAsync(erp, connection: On);

        erp.Calls.Skip(before).Should().BeEmpty("a created supplier is never pushed again, so the sweep writes nothing");
    }

    public static TheoryData<string> ClosedConnections => new() { "switch off", "connection disabled", "no group", "no connection" };

    [Theory]
    [MemberData(nameof(ClosedConnections))]
    public async Task Nothing_is_sent_unless_the_connection_is_enabled_with_the_switch_on_and_a_default_group(string closed)
    {
        var connection = closed switch
        {
            "switch off" => On with { CreateSuppliersInErp = false },
            "connection disabled" => On with { IsEnabled = false },
            "no group" => On with { DefaultSupplierGroup = " " },
            _ => null,
        };
        var approved = await ApprovedAsync();
        var erp = new FakeErp();

        await PushAsync(erp, approved.Id, connection);

        erp.Calls.Should().BeEmpty();
        var untouched = await ReadAsync(approved.Id);
        AssertUntouched(untouched);
        untouched.ErpPushStartedAt.Should().BeNull();

        await PushOneAsync(erp, approved.Id);
        erp.Writes.Should().Contain(SupplierPost, "the same supplier is pushed once the connection allows it");
    }

    [Fact]
    public async Task A_supplier_from_the_erp_is_never_pushed_even_when_it_is_approved_again()
    {
        var externalId = $"SUP-IMPORTED-{Guid.NewGuid():N}";
        var referenceCode = $"SUP-I-{Guid.NewGuid():N}"[..24];

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var imported = Supplier.ImportFromErp(
                referenceCode, externalId, Unique("Imported Co"), null, SupplierLegalType.Company, "SYP", "Imported Co",
                $"imported-{Guid.NewGuid():N}@push.example", null);
            db.Suppliers.Add(imported);
            await db.SaveChangesAsync();

            imported.UpdateLegalInfo(
                "شركة مستوردة", "Imported Co", null, "TAX-IMPORTED", SupplierLegalType.Company, null, isComplianceCritical: true)
                .Should().BeTrue("a compliance edit sends an approved supplier back to review");
            await db.SaveChangesAsync();
        }

        var importedId = await IdOfAsync(referenceCode);
        await ApproveEveryRequiredDocumentAsync(importedId);
        var jobs = new RecordingJobClient();
        await ApproveAsync(referenceCode, jobs);

        jobs.Enqueued.Should().NotContain(e => e.Type == typeof(SupplierErpPushJob));
        (await ReadAsync(importedId)).ErpPushStatus.Should().Be(SupplierErpPushStatus.NotRequested);

        var registered = await ApprovedAsync();
        var erp = new FakeErp();
        await PushAsync(erp, connection: On);

        erp.Suppliers.Should().ContainSingle("the sweep ran, and created only the supplier that registered here")
            .Which.Body["supplier_name"]!.GetValue<string>().Should().Be(registered.Name);
        (await ReadAsync(importedId)).ExternalId.Should().Be(externalId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_create_whose_link_was_not_saved_is_found_and_linked_by_the_next_run_without_a_second_create(bool withTaxId)
    {
        var approved = await ApprovedAsync(withTaxId ? $"TAX-{Guid.NewGuid():N}"[..20] : null);
        var erp = new FakeErp();

        await PushOneAsync(erp, approved.Id, audit: inner => new RefusingAudit(inner, "supplier.erp_push_created"));

        erp.Suppliers.Should().ContainSingle("the ERP created the supplier before the portal failed to save its name");
        var unlinked = await ReadAsync(approved.Id);
        unlinked.ExternalId.Should().BeNull("the save that holds the name was the one that failed");
        unlinked.ErpPushStatus.Should().Be(SupplierErpPushStatus.Requested);
        unlinked.ErpPushLastError.Should().NotBeNull();

        await MakeDueAsync(approved.Id);
        var mark = erp.Calls.Count;
        await PushOneAsync(erp, approved.Id);

        erp.Calls.Skip(mark).Should().Contain(approved.TaxId is null ? PortalCreatesSearch : TaxIdSearch);
        erp.Writes.Count(w => w == SupplierPost).Should().Be(1, "the ERP makes a second supplier for a second request");
        var supplier = await ReadAsync(approved.Id);
        supplier.ExternalId.Should().Be(erp.Suppliers.Single().Name);
        supplier.ErpPushStatus.Should().Be(SupplierErpPushStatus.Created);
    }

    [Fact]
    public async Task A_create_whose_answer_was_lost_is_looked_up_and_never_sent_twice()
    {
        var approved = await ApprovedAsync();
        var erp = new FakeErp
        {
            FailAfter = call => call == SupplierPost
                ? Refusal(HttpStatusCode.BadGateway, HttpMethod.Post, erpMessage: "no answer before the timeout")
                : null,
        };

        await PushOneAsync(erp, approved.Id);

        erp.Calls.Should().ContainInOrder(SupplierPost, PortalCreatesSearch, AddressPost);
        erp.Writes.Count(w => w == SupplierPost).Should().Be(1);
        var supplier = await ReadAsync(approved.Id);
        supplier.ExternalId.Should().Be(erp.Suppliers.Single().Name);
        supplier.ErpPushStatus.Should().Be(SupplierErpPushStatus.Created);
    }

    [Fact]
    public async Task A_create_that_got_no_answer_and_did_not_happen_waits_and_the_next_run_looks_before_it_creates()
    {
        var approved = await ApprovedAsync();
        var refused = false;
        var erp = new FakeErp
        {
            FailBefore = call =>
            {
                if (call != SupplierPost || refused) return null;
                refused = true;
                return Refusal(HttpStatusCode.RequestTimeout, HttpMethod.Post);
            },
        };

        await PushOneAsync(erp, approved.Id);

        erp.Suppliers.Should().BeEmpty();
        var waiting = await ReadAsync(approved.Id);
        waiting.ErpPushStatus.Should().Be(SupplierErpPushStatus.Requested);
        waiting.ErpPushAttempts.Should().Be(1);
        waiting.ErpPushLastError.Should().Contain("looks again before it creates");
        waiting.ErpPushNextAttemptAt.Should().BeAfter(DateTimeOffset.UtcNow);

        await MakeDueAsync(approved.Id);
        var mark = erp.Calls.Count;
        await PushOneAsync(erp, approved.Id);

        erp.Calls.Skip(mark).Should().ContainInOrder(PortalCreatesSearch, SupplierPost);
        erp.Suppliers.Should().ContainSingle();
        (await ReadAsync(approved.Id)).ErpPushStatus.Should().Be(SupplierErpPushStatus.Created);
    }

    [Fact]
    public async Task A_contact_that_failed_after_the_supplier_was_created_leaves_it_linked_and_the_next_run_makes_only_what_is_missing()
    {
        var approved = await ApprovedAsync();
        var refused = false;
        var erp = new FakeErp
        {
            FailBefore = call =>
            {
                if (call != ContactPost || refused) return null;
                refused = true;
                return Refusal(HttpStatusCode.ServiceUnavailable, HttpMethod.Post);
            },
        };

        await PushOneAsync(erp, approved.Id);

        var linked = await ReadAsync(approved.Id);
        linked.ErpPushStatus.Should().Be(SupplierErpPushStatus.Linked);
        linked.ExternalId.Should().Be(erp.Suppliers.Single().Name);
        linked.ErpPushAttempts.Should().Be(1);
        linked.ErpPushNextAttemptAt.Should().BeAfter(DateTimeOffset.UtcNow);

        await MakeDueAsync(approved.Id);
        var mark = erp.Calls.Count;
        await PushOneAsync(erp, approved.Id);

        erp.Calls.Skip(mark).Where(FakeErp.IsWrite).Should()
            .Equal(ContactPost, UserPost, PortalUsersPut, ContactUserPut);
        erp.Suppliers.Should().ContainSingle();
        erp.Addresses.Should().ContainSingle();
        (await ReadAsync(approved.Id)).ErpPushStatus.Should().Be(SupplierErpPushStatus.Created);
    }

    [Fact]
    public async Task A_user_that_failed_after_the_contact_was_made_is_created_and_the_contact_pointed_at_it_next_run()
    {
        var approved = await ApprovedAsync();
        var refused = false;
        var erp = new FakeErp
        {
            FailBefore = call =>
            {
                if (call != UserPost || refused) return null;
                refused = true;
                return Refusal(HttpStatusCode.ServiceUnavailable, HttpMethod.Post);
            },
        };

        await PushOneAsync(erp, approved.Id);
        (await ReadAsync(approved.Id)).ErpPushStatus.Should().Be(SupplierErpPushStatus.Linked);

        await MakeDueAsync(approved.Id);
        var mark = erp.Calls.Count;
        await PushOneAsync(erp, approved.Id);

        erp.Calls.Skip(mark).Where(FakeErp.IsWrite).Should().Equal(
            [UserPost, PortalUsersPut, ContactUserPut],
            "the contact made before the user is pointed at it, as the colleague's sixth call does");
        erp.Contacts.Should().ContainSingle().Which.User.Should().Be(approved.Email);
        (await ReadAsync(approved.Id)).ErpPushStatus.Should().Be(SupplierErpPushStatus.Created);
    }

    [Fact]
    public async Task A_website_user_the_erp_already_has_is_used_and_the_new_contact_is_not_pointed_at_it_again()
    {
        var approved = await ApprovedAsync();
        var erp = new FakeErp();
        erp.Users.Add(approved.Email);

        await PushOneAsync(erp, approved.Id);

        erp.Writes.Should().Equal(
            [SupplierPost, AddressPost, ContactPost, PortalUsersPut],
            "the ERP links a contact made after the user by itself, and the user is not created twice");
        erp.PortalUsers.Values.Should().ContainSingle().Which.Should().Equal(approved.Email);
        (await ReadAsync(approved.Id)).ErpPushStatus.Should().Be(SupplierErpPushStatus.Created);
    }

    [Fact]
    public async Task A_linked_supplier_sent_back_to_review_waits_and_carries_on_once_it_is_approved_again()
    {
        var approved = await ApprovedAsync();
        var refused = false;
        var erp = new FakeErp
        {
            FailBefore = call =>
            {
                if (call != ContactPost || refused) return null;
                refused = true;
                return Refusal(HttpStatusCode.TooManyRequests, HttpMethod.Post);
            },
        };
        await PushOneAsync(erp, approved.Id);

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var supplier = await db.Suppliers.SingleAsync(s => s.Id == approved.Id);
            supplier.UpdateLegalInfo(
                "شركة الدفع", approved.Name, supplier.LegalInfo!.RegistrationNumber, "TAX-EDITED", SupplierLegalType.Company,
                null, isComplianceCritical: true).Should().BeTrue();
            await db.SaveChangesAsync();
        }

        await MakeDueAsync(approved.Id);
        var mark = erp.Calls.Count;
        await PushAsync(erp, connection: On);
        erp.Calls.Skip(mark).Should().BeEmpty("a supplier under review again is not pushed until it is approved again");

        var jobs = new RecordingJobClient();
        await ApproveAsync(approved.ReferenceCode, jobs);
        jobs.Enqueued.Should().NotContain(e => e.Type == typeof(SupplierErpPushJob),
            "approval asks only for a supplier the ERP does not hold yet; the sweep carries this one on");

        await PushAsync(erp, connection: On);

        erp.Writes.Count(w => w == SupplierPost).Should().Be(1);
        erp.Contacts.Should().ContainSingle();
        (await ReadAsync(approved.Id)).ErpPushStatus.Should().Be(SupplierErpPushStatus.Created);
    }

    [Fact]
    public async Task An_erp_supplier_another_portal_supplier_carries_is_never_taken_for_this_one()
    {
        var taxId = $"TAX-{Guid.NewGuid():N}"[..20];
        var carried = $"SUP-CARRIED-{Guid.NewGuid():N}";

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Suppliers.Add(Supplier.ImportFromErp(
                $"SUP-C-{Guid.NewGuid():N}"[..24], carried, Unique("Carried Co"), taxId, SupplierLegalType.Company, "SYP",
                "Carried Co", $"carried-{Guid.NewGuid():N}@push.example", null));
            await db.SaveChangesAsync();
        }

        var approved = await ApprovedAsync(taxId);
        var erp = new FakeErp
        {
            FailAfter = call => call == SupplierPost ? Refusal(HttpStatusCode.GatewayTimeout, HttpMethod.Post) : null,
        };
        erp.Suppliers.Add(new FakeSupplier(carried, "Carried Co", taxId, DateTimeOffset.UtcNow.AddYears(-1), []));

        await PushOneAsync(erp, approved.Id);

        var ours = erp.Suppliers.Single(s => s.Name != carried).Name;
        var supplier = await ReadAsync(approved.Id);
        supplier.ExternalId.Should().Be(ours, "the import already matches the other ERP supplier to its own portal supplier");
        supplier.ErpPushStatus.Should().Be(SupplierErpPushStatus.Created);
        erp.Writes.Count(w => w == SupplierPost).Should().Be(1);
    }

    [Fact]
    public async Task Several_erp_suppliers_that_could_be_this_one_fail_the_push_naming_them()
    {
        var taxId = $"TAX-{Guid.NewGuid():N}"[..20];
        var approved = await ApprovedAsync(taxId);
        var erp = new FakeErp
        {
            FailAfter = call => call == SupplierPost ? Refusal(HttpStatusCode.GatewayTimeout, HttpMethod.Post) : null,
        };
        var older = $"SUP-OLDER-{Guid.NewGuid():N}";
        erp.Suppliers.Add(new FakeSupplier(older, "Older Co", taxId, DateTimeOffset.UtcNow.AddYears(-1), []));

        await PushOneAsync(erp, approved.Id);

        var failed = await ReadAsync(approved.Id);
        failed.ErpPushStatus.Should().Be(SupplierErpPushStatus.Failed);
        failed.ExternalId.Should().BeNull("the portal cannot tell which of them it created");
        failed.ErpPushLastError.Should().Contain(older).And.Contain(erp.Suppliers.Single(s => s.Name != older).Name);
        erp.Writes.Should().Equal(SupplierPost);
    }

    [Theory]
    [InlineData(SupplierFieldsRead)]
    [InlineData(SupplierPost)]
    public async Task A_refused_credential_stops_the_run_and_records_nothing_on_any_supplier(string refusedCall)
    {
        var first = await ApprovedAsync();
        var second = await ApprovedAsync();
        var erp = new FakeErp
        {
            FailBefore = call => call == refusedCall
                ? Refusal(HttpStatusCode.Forbidden, refusedCall.StartsWith("POST") ? HttpMethod.Post : HttpMethod.Get)
                : null,
        };

        await PushAsync(erp, connection: On);

        erp.Calls.Count(c => c == refusedCall).Should().Be(1, "the run stops at the first refusal, not once per supplier");
        erp.Suppliers.Should().BeEmpty();
        AssertUntouched(await ReadAsync(first.Id));
        AssertUntouched(await ReadAsync(second.Id));
        (await ReadAsync(second.Id)).ErpPushStartedAt.Should().BeNull("the run never reached the second supplier");
        (await PushTrailAsync(first.Id)).Should().BeEmpty();
        (await PushTrailAsync(second.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_refusal_the_erp_will_repeat_fails_the_push_with_the_erps_own_message()
    {
        var approved = await ApprovedAsync();
        var erp = new FakeErp
        {
            FailBefore = call => call == SupplierPost
                ? Refusal(HttpStatusCode.ExpectationFailed, HttpMethod.Post, "MandatoryError",
                    "Value missing for Supplier: Supplier Group")
                : null,
        };

        await PushOneAsync(erp, approved.Id);

        var failed = await ReadAsync(approved.Id);
        failed.ErpPushStatus.Should().Be(SupplierErpPushStatus.Failed);
        failed.ErpPushLastError.Should().Contain("Value missing for Supplier: Supplier Group");
        failed.ErpPushNextAttemptAt.Should().BeNull();

        var trail = await PushTrailAsync(approved.Id);
        trail.Should().ContainSingle().Which.Should().Match<MotsSupplierPortal.Domain.Audit.AuditLog>(a =>
            a.Action == "supplier.erp_push_failed" && a.Reason!.Contains("Value missing for Supplier: Supplier Group"));

        var mark = erp.Calls.Count;
        await PushAsync(erp, connection: On);
        erp.Calls.Skip(mark).Should().BeEmpty("a failed push waits for a person");
    }

    [Fact]
    public async Task A_passing_failure_waits_longer_each_time_and_the_eighth_fails_the_push()
    {
        var approved = await ApprovedAsync();
        var erp = new FakeErp
        {
            FailBefore = call => call == SupplierPost ? Refusal(HttpStatusCode.TooManyRequests, HttpMethod.Post) : null,
        };
        var waits = new List<TimeSpan>();

        for (var attempt = 1; attempt < SupplierErpPushJob.MaxAttempts; attempt++)
        {
            await MakeDueAsync(approved.Id);
            var started = DateTimeOffset.UtcNow;
            await PushOneAsync(erp, approved.Id);

            var waiting = await ReadAsync(approved.Id);
            waiting.ErpPushStatus.Should().Be(SupplierErpPushStatus.Requested);
            waiting.ErpPushAttempts.Should().Be(attempt);
            waits.Add(waiting.ErpPushNextAttemptAt!.Value - started);
        }

        waits.Select(w => (int)Math.Round(w.TotalMinutes)).Should().Equal(1, 5, 15, 60, 60, 60, 60);

        await MakeDueAsync(approved.Id);
        await PushOneAsync(erp, approved.Id);

        var failed = await ReadAsync(approved.Id);
        failed.ErpPushStatus.Should().Be(SupplierErpPushStatus.Failed);
        failed.ErpPushAttempts.Should().Be(SupplierErpPushJob.MaxAttempts);
        failed.ErpPushLastError.Should().StartWith("Stopped after 8 failed attempts.");
        erp.Suppliers.Should().BeEmpty();
    }

    [Fact]
    public async Task A_payload_the_builder_holds_fails_the_push_without_writing_to_the_erp()
    {
        var approved = await ApprovedAsync(country: "Lebanon");
        var erp = new FakeErp();

        await PushOneAsync(erp, approved.Id);

        erp.Writes.Should().BeEmpty();
        var failed = await ReadAsync(approved.Id);
        failed.ErpPushStatus.Should().Be(SupplierErpPushStatus.Failed);
        failed.ErpPushLastError.Should().Contain("only Syria is mapped");
    }

    [Fact]
    public async Task The_push_steps_aside_while_an_import_holds_the_lock()
    {
        var approved = await ApprovedAsync();
        var erp = new FakeErp();

        await using var holderScope = fixture.Services.CreateAsyncScope();
        var holder = holderScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await holder.Database.OpenConnectionAsync();
        try
        {
            (await holder.Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock({ErpLockKey}) AS \"Value\"").SingleAsync())
                .Should().BeTrue("the test must hold the lock for this to prove anything");

            await PushOneAsync(erp, approved.Id);
        }
        finally
        {
            await holder.Database.SqlQuery<bool>($"SELECT pg_advisory_unlock({ErpLockKey}) AS \"Value\"").SingleAsync();
            await holder.Database.CloseConnectionAsync();
        }

        erp.Calls.Should().BeEmpty();
        AssertUntouched(await ReadAsync(approved.Id));

        await PushOneAsync(erp, approved.Id);
        erp.Writes.Should().Contain(SupplierPost, "the next run picks the supplier up once the lock is free");
    }

    [Fact]
    public async Task An_import_cannot_run_while_the_push_is_creating_a_supplier()
    {
        var approved = await ApprovedAsync();
        Exception? import = null;
        var erp = new FakeErp
        {
            DuringSupplierCreate = async () =>
            {
                await using var scope = fixture.Services.CreateAsyncScope();
                try
                {
                    await ImportHandler(scope).HandleAsync(ErpImportTrigger.Manual, CancellationToken.None);
                }
                catch (Exception refused)
                {
                    import = refused;
                }
            },
        };

        await PushOneAsync(erp, approved.Id);

        import.Should().BeOfType<ErpImportBusyException>(
            "an import between the ERP's create and the saved name would create the supplier a second time");
        (await ReadAsync(approved.Id)).ErpPushStatus.Should().Be(SupplierErpPushStatus.Created);
    }

    [Fact]
    public async Task The_import_holds_a_pushed_supplier_while_the_erp_has_it_in_draft_and_releases_it_once_approved()
    {
        var approved = await ApprovedAsync(taxId: $"TAX-{Guid.NewGuid():N}"[..20]);
        var erp = new FakeErp();
        await PushOneAsync(erp, approved.Id);
        var erpName = erp.Suppliers.Single().Name;

        var inErp = ErpSupplierTestFactory.Supplier(erpName) with
        {
            Name = approved.Name,
            TaxId = approved.TaxId,
            Email = approved.Email,
            WorkflowState = "Draft",
        };

        var draft = await ImportAsync(inErp);

        draft.Created.Should().Be(0, "the pushed supplier is matched by its ExternalId, not imported again");
        var held = await ReadAsync(approved.Id);
        held.LifecycleState.Should().Be(SupplierLifecycleState.Suspended);
        held.ErpDisabledState.Should().Be(SupplierErpDisabledState.SuspendedAsPending);

        await ImportAsync(inErp with { WorkflowState = "Approved" });

        var released = await ReadAsync(approved.Id);
        released.LifecycleState.Should().Be(SupplierLifecycleState.Active);
        released.ErpDisabledState.Should().Be(SupplierErpDisabledState.NotDisabled);
        released.ErpPushStatus.Should().Be(SupplierErpPushStatus.Created);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Suppliers.CountAsync(s => s.ExternalId == erpName)).Should().Be(1);
        (await db.Suppliers.CountAsync(s => s.DisplayNameEn == approved.Name)).Should().Be(1, "no second portal supplier");
    }

    private async Task SendBackToReviewAndApproveAsync(Approved approved)
    {
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var supplier = await db.Suppliers.SingleAsync(s => s.Id == approved.Id);
            supplier.UpdateLegalInfo(
                "شركة الدفع المعدلة", approved.Name, supplier.LegalInfo!.RegistrationNumber, supplier.LegalInfo.TaxId,
                supplier.LegalInfo.SupplierType, null, isComplianceCritical: true).Should().BeTrue();
            await db.SaveChangesAsync();
        }

        await ApproveAsync(approved.ReferenceCode, new RecordingJobClient());
    }

    private async Task<int> CarryingAsync(string erpName)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Suppliers.CountAsync(s => s.ExternalId == erpName);
    }

    private async Task ChangeLifecycleAsync(Guid supplierId, params Action<Supplier>[] changes)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplier = await db.Suppliers.SingleAsync(s => s.Id == supplierId);
        foreach (var change in changes) change(supplier);
        await db.SaveChangesAsync();
    }

    private static FakeErp LosingTheCreatesAnswer() => new()
    {
        FailAfter = call => call == SupplierPost ? Refusal(HttpStatusCode.GatewayTimeout, HttpMethod.Post) : null,
        Lagging = true,
    };

    // THE IMPORT BETWEEN TWO ATTEMPTS. The create's answer is lost, the look straight after misses the record because
    // the ERP has not committed it yet, and the lock is free until the next attempt, so the hourly import runs in that
    // gap. The first version created a portal supplier from the record, and the push, finding it carried, posted a
    // second one: two suppliers in each system for one company.
    [Fact]
    public async Task An_import_between_two_attempts_leaves_the_push_its_own_create_and_the_push_links_it()
    {
        var approved = await ApprovedAsync();
        var erp = LosingTheCreatesAnswer();

        await PushOneAsync(erp, approved.Id);

        var orphan = erp.Suppliers.Single().Name;
        (await ReadAsync(approved.Id)).ExternalId.Should().BeNull("the look straight after the lost answer found nothing");

        var report = await ImportAsync(ErpSupplierTestFactory.Supplier(orphan) with
        {
            Name = approved.Name,
            WorkflowState = "Draft",
            CreatedByPortal = true,
        });

        report.Created.Should().Be(0, "the portal's API user created it, so it is the push's to link, not a newcomer");
        var held = report.Rows.Should().ContainSingle(r => r.ExternalId == orphan).Subject;
        held.Outcome.Should().Be(ErpImportOutcome.Refused);
        held.Notes.Should().Equal(ErpImportPreviewBuilder.HeldForPushNote);
        (await CarryingAsync(orphan)).Should().Be(0);

        erp.Lagging = false;
        await MakeDueAsync(approved.Id);
        await PushOneAsync(erp, approved.Id);

        erp.Writes.Count(w => w == SupplierPost).Should().Be(1, "the ERP makes a second supplier for a second request");
        var supplier = await ReadAsync(approved.Id);
        supplier.ExternalId.Should().Be(orphan);
        supplier.ErpPushStatus.Should().Be(SupplierErpPushStatus.Created);
        (await CarryingAsync(orphan)).Should().Be(1);
    }

    // A RECORD ANOTHER PORTAL SUPPLIER CARRIES IS NOT POSTED PAST. Here the orphan was already made into a portal
    // supplier - by an import from before it learnt to leave the portal's creates alone, say. The first version dropped
    // the carried match, found nothing else, and posted a second supplier.
    [Fact]
    public async Task A_record_another_portal_supplier_carries_is_not_posted_past_and_the_push_fails_naming_both()
    {
        var approved = await ApprovedAsync();
        var erp = LosingTheCreatesAnswer();
        await PushOneAsync(erp, approved.Id);
        var orphan = erp.Suppliers.Single().Name;

        var carrier = $"SUP-D-{Guid.NewGuid():N}"[..24];
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Suppliers.Add(Supplier.ImportFromErp(
                carrier, orphan, approved.Name, null, SupplierLegalType.Company, "SYP", approved.Name,
                $"carrier-{Guid.NewGuid():N}@push.example", null));
            await db.SaveChangesAsync();
        }

        erp.Lagging = false;
        await MakeDueAsync(approved.Id);
        await PushOneAsync(erp, approved.Id);

        erp.Writes.Should().Equal([SupplierPost], "a second post would give the ERP another copy whichever it is");
        var failed = await ReadAsync(approved.Id);
        failed.ErpPushStatus.Should().Be(SupplierErpPushStatus.Failed, "only a person can say which company that is");
        failed.ExternalId.Should().BeNull();
        failed.ErpPushLastError.Should().Contain(orphan).And.Contain(carrier);
    }

    // A SECOND APPROVAL KEEPS THE LOOK REACHING BACK. The first version moved the request time to the new approval, so
    // a create lost an hour before it fell outside the look, and a supplier with no tax number was posted again.
    [Fact]
    public async Task A_second_approval_keeps_the_look_reaching_back_to_a_create_lost_before_it()
    {
        var approved = await ApprovedAsync();
        var erp = LosingTheCreatesAnswer();
        await PushOneAsync(erp, approved.Id);

        var anHourAgo = DateTimeOffset.UtcNow.AddHours(-1);
        erp.Suppliers[0] = erp.Suppliers[0] with { CreatedAt = anHourAgo };
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Suppliers
                .Where(s => s.Id == approved.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.ErpPushRequestedAt, anHourAgo.AddMinutes(-1)));
        }

        await SendBackToReviewAndApproveAsync(approved);

        (await ReadAsync(approved.Id)).ErpPushRequestedAt.Should().BeCloseTo(
            anHourAgo.AddMinutes(-1), TimeSpan.FromMilliseconds(1), "a later approval keeps the first request's time");

        erp.Lagging = false;
        await PushOneAsync(erp, approved.Id);

        erp.Writes.Count(w => w == SupplierPost).Should().Be(1);
        (await ReadAsync(approved.Id)).ExternalId.Should().Be(erp.Suppliers.Single().Name);
    }

    // A SUPPLIER A PERSON TOOK OUT OF SERVICE IS NOT PUSHED. The switch is off by default, so approvals wait; one
    // suspended or deactivated meanwhile would otherwise be created in the ERP with a website user holding the Supplier
    // role for a company whose access here was withdrawn.
    [Theory]
    [InlineData(SupplierLifecycleState.Suspended)]
    [InlineData(SupplierLifecycleState.Deactivated)]
    public async Task A_supplier_a_person_took_out_of_service_is_not_pushed_until_it_is_back(
        SupplierLifecycleState lifecycle)
    {
        var approved = await ApprovedAsync();
        await ChangeLifecycleAsync(
            approved.Id,
            lifecycle == SupplierLifecycleState.Deactivated
                ? [s => s.Suspend("Fraud under investigation."), s => s.Deactivate("Fraud confirmed.")]
                : [s => s.Suspend("Under investigation.")]);
        var erp = new FakeErp();

        await PushOneAsync(erp, approved.Id);
        await PushAsync(erp, connection: On);

        erp.Calls.Should().BeEmpty();
        var waiting = await ReadAsync(approved.Id);
        waiting.ErpPushStatus.Should().Be(SupplierErpPushStatus.Requested, "its push stays where it is");
        waiting.ErpPushAttempts.Should().Be(0);

        if (lifecycle == SupplierLifecycleState.Deactivated) return;

        await ChangeLifecycleAsync(approved.Id, s => s.Reactivate("Cleared."));
        await PushOneAsync(erp, approved.Id);

        erp.Writes.Should().Contain(SupplierPost, "back in service, its push goes on");
        (await ReadAsync(approved.Id)).ErpPushStatus.Should().Be(SupplierErpPushStatus.Created);
    }

    // THE SYNC'S HOLD FOR THE ERP'S APPROVAL IS NOT OUT OF SERVICE. The Draft the push made is what put the supplier
    // there, and the contact and the website user are part of what the ERP team approves.
    [Fact]
    public async Task A_linked_push_the_import_holds_for_the_erps_approval_still_completes()
    {
        var approved = await ApprovedAsync();
        var refused = false;
        var erp = new FakeErp
        {
            FailBefore = call =>
            {
                if (call != ContactPost || refused) return null;
                refused = true;
                return Refusal(HttpStatusCode.ServiceUnavailable, HttpMethod.Post);
            },
        };
        await PushOneAsync(erp, approved.Id);
        var erpName = erp.Suppliers.Single().Name;

        await ImportAsync(ErpSupplierTestFactory.Supplier(erpName) with
        {
            Name = approved.Name,
            Email = approved.Email,
            WorkflowState = "Draft",
            CreatedByPortal = true,
        });

        var held = await ReadAsync(approved.Id);
        held.LifecycleState.Should().Be(SupplierLifecycleState.Suspended);
        held.ErpDisabledState.Should().Be(SupplierErpDisabledState.SuspendedAsPending);
        held.ErpPushStatus.Should().Be(SupplierErpPushStatus.Linked);

        await MakeDueAsync(approved.Id);
        await PushOneAsync(erp, approved.Id);

        erp.Contacts.Should().ContainSingle();
        erp.Users.Should().Contain(approved.Email);
        (await ReadAsync(approved.Id)).ErpPushStatus.Should().Be(SupplierErpPushStatus.Created);
    }

    // EVERY WRITE MOVES THE VERSION. The first version left it alone, so a save through the record that had read the
    // push before - a reviewer approving again, say - went through and wrote over, or lost, what the push recorded.
    [Fact]
    public async Task Every_write_the_push_makes_moves_the_version_so_a_save_that_read_it_before_is_refused()
    {
        var approved = await ApprovedAsync();

        await using var staleScope = fixture.Services.CreateAsyncScope();
        var staleDb = staleScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stale = await staleDb.Suppliers.SingleAsync(s => s.Id == approved.Id);
        var versionBefore = stale.RowVersion;

        await PushOneAsync(new FakeErp(), approved.Id);

        var pushed = await ReadAsync(approved.Id);
        pushed.ErpPushStatus.Should().Be(SupplierErpPushStatus.Created);
        pushed.RowVersion.Should().Be(versionBefore + 3, "the marker, the link and the completion are each a write");

        stale.UpdateCoreProfile("Edited on a page read before the push.", null, "SME", "SYP");
        var save = () => staleDb.SaveChangesAsync();

        await save.Should().ThrowAsync<DbUpdateConcurrencyException>(
            "a save that read the push as it was before must be told to read again, not write over it");
    }

    // THE ERP NAMES A RECORD IN UP TO 140 CHARACTERS, and a server that names suppliers by supplier_name lets that name
    // be as long. The column held 100, so the link could never be saved and every attempt found the record and failed.
    [Fact]
    public async Task An_erp_name_as_long_as_the_erp_allows_is_saved_as_the_link()
    {
        var approved = await ApprovedAsync();
        var longest = $"LONG-{Guid.NewGuid():N}".PadRight(140, 'x');
        var erp = new FakeErp { NextName = longest };

        await PushOneAsync(erp, approved.Id);

        var supplier = await ReadAsync(approved.Id);
        supplier.ExternalId.Should().Be(longest);
        supplier.ErpPushStatus.Should().Be(SupplierErpPushStatus.Created);
    }

    // A PUSHED PARTNERSHIP COMES BACK A PARTNERSHIP. The push sends the type as the portal spells it, and the import
    // read everything but Individual as a Company, so every hourly run rewrote a partnership the portal had approved.
    [Fact]
    public async Task A_pushed_partnership_is_still_a_partnership_after_the_import()
    {
        var approved = await ApprovedAsync(legalType: SupplierLegalType.Partnership);
        var erp = new FakeErp();
        await PushOneAsync(erp, approved.Id);
        var created = erp.Suppliers.Single();
        created.Body["supplier_type"]!.GetValue<string>().Should().Be("Partnership");

        await ImportAsync(ErpSupplierTestFactory.Supplier(created.Name) with
        {
            Name = approved.Name,
            Email = approved.Email,
            LegalType = "Partnership",
            CreatedByPortal = true,
        });

        (await ReadAsync(approved.Id)).LegalInfo!.SupplierType.Should().Be(SupplierLegalType.Partnership);
    }

    private sealed record FakeSupplier(string Name, string SupplierName, string? TaxId, DateTimeOffset CreatedAt, JsonObject Body);

    private sealed record FakeAddress(string Name, string Supplier, string? Line1, string? City);

    private sealed record FakeContact(string Name, string Supplier, string? Email)
    {
        public string? User { get; set; }
    }

    // The ERP as far as the push can see it, answering from what it was sent. FailBefore refuses a call before the record
    // is made; FailAfter refuses it after, which is a create whose answer was lost.
    private sealed class FakeErp : IErpSupplierRegistrar
    {
        public List<string> Calls { get; } = [];
        public List<FakeSupplier> Suppliers { get; } = [];
        public List<FakeAddress> Addresses { get; } = [];
        public List<FakeContact> Contacts { get; } = [];
        public List<string> Users { get; } = [];
        public Dictionary<string, List<string>> PortalUsers { get; } = [];
        public Func<string, Exception?> FailBefore { get; init; } = _ => null;
        public Func<string, Exception?> FailAfter { get; init; } = _ => null;
        public Func<Task>? DuringSupplierCreate { get; init; }

        public bool Lagging { get; set; }

        public string? NextName { get; set; }

        public IEnumerable<string> Writes => Calls.Where(IsWrite);

        public static bool IsWrite(string call) => call.StartsWith("POST", StringComparison.Ordinal)
            || call.StartsWith("PUT", StringComparison.Ordinal);

        public Task<ErpRecordFields> ReadFieldsAsync(string recordType, CancellationToken ct)
        {
            Before($"GET fields {recordType}");

            return Task.FromResult(new ErpRecordFields(recordType, recordType switch
            {
                ErpRecordType.Supplier =>
                [
                    Field("naming_series", "Select", "SUP-.YYYY.-.#####"), Field("supplier_name"),
                    Field(ErpSupplierPayload.ArabicNameField), Field("supplier_type", "Select", "Company", "Individual", "Partnership"),
                    Field("supplier_group", "Link"), Field("country", "Link"), Field("default_currency", "Link"),
                    Field("tax_id"), Field(ErpSupplierPayload.RegistrationNumberField),
                    Field(ErpSupplierPayload.RegistrationTypeField), Field("supplier_details", "Text"), Field("website"),
                    Field("portal_users", "Table"),
                ],
                ErpRecordType.Address =>
                [
                    Field("address_title"), Field("address_type", "Select", "Billing", "Shipping", "Office"),
                    Field("address_line1"), Field("address_line2"), Field("city"), Field("state"), Field("country", "Link"),
                    Field("pincode"), Field("is_primary_address", "Check"), Field("links", "Table"),
                ],
                _ =>
                [
                    Field("first_name"), Field("last_name"), Field("email_ids", "Table"), Field("phone_nos", "Table"),
                    Field("designation"), Field("is_primary_contact", "Check"), Field("links", "Table"), Field("user", "Link"),
                ],
            }));
        }

        public Task<IReadOnlyList<ErpSupplierMatch>> FindSuppliersByTaxIdAsync(string taxId, CancellationToken ct)
        {
            Before(TaxIdSearch);
            return Task.FromResult<IReadOnlyList<ErpSupplierMatch>>(
                Lagging ? [] : [.. Suppliers.Where(s => s.TaxId == taxId).Select(Match)]);
        }

        public Task<IReadOnlyList<ErpSupplierMatch>> FindSuppliersCreatedByPortalAsync(
            string supplierName, DateTimeOffset since, CancellationToken ct)
        {
            Before(PortalCreatesSearch);
            return Task.FromResult<IReadOnlyList<ErpSupplierMatch>>(Lagging
                ? []
                : [.. Suppliers.Where(s => s.SupplierName == supplierName && s.CreatedAt >= since).Select(Match)]);
        }

        public async Task<string> CreateSupplierAsync(JsonObject body, CancellationToken ct)
        {
            Before(SupplierPost);

            if (DuringSupplierCreate is not null) await DuringSupplierCreate();

            var name = NextName ?? $"PUSH-{Guid.NewGuid():N}";
            NextName = null;
            Suppliers.Add(new FakeSupplier(
                name, body["supplier_name"]!.GetValue<string>(), body["tax_id"]?.GetValue<string>(), DateTimeOffset.UtcNow, body));

            After(SupplierPost);
            return name;
        }

        public Task<string> CreateAddressAsync(JsonObject body, CancellationToken ct)
        {
            Before(AddressPost);
            var name = $"{body["address_title"]!.GetValue<string>()}-Billing-{Addresses.Count}";
            Addresses.Add(new FakeAddress(
                name, LinkOf(body), body["address_line1"]?.GetValue<string>(), body["city"]?.GetValue<string>()));
            After(AddressPost);
            return Task.FromResult(name);
        }

        public Task<string> CreateContactAsync(JsonObject body, CancellationToken ct)
        {
            Before(ContactPost);
            var name = $"{body["first_name"]!.GetValue<string>()}-{Contacts.Count}";
            Contacts.Add(new FakeContact(name, LinkOf(body), body["email_ids"]?[0]?["email_id"]?.GetValue<string>()));
            After(ContactPost);
            return Task.FromResult(name);
        }

        public Task<string> CreateUserAsync(JsonObject body, CancellationToken ct)
        {
            Before(UserPost);
            var email = body["email"]!.GetValue<string>();
            Users.Add(email);
            After(UserPost);
            return Task.FromResult(email);
        }

        public Task<string?> FindUserAsync(string email, CancellationToken ct)
        {
            Before("GET User");
            return Task.FromResult(Users.FirstOrDefault(u => u == email));
        }

        public Task<IReadOnlyList<ErpLinkedAddress>> ListLinkedAddressesAsync(string erpSupplierName, CancellationToken ct)
        {
            Before("GET Address links");
            return Task.FromResult<IReadOnlyList<ErpLinkedAddress>>(
                [.. Addresses.Where(a => a.Supplier == erpSupplierName)
                    .Select(a => new ErpLinkedAddress(a.Name, "Billing", a.Line1, a.City))]);
        }

        public Task<IReadOnlyList<ErpLinkedContact>> ListLinkedContactsAsync(string erpSupplierName, CancellationToken ct)
        {
            Before("GET Contact links");
            return Task.FromResult<IReadOnlyList<ErpLinkedContact>>(
                [.. Contacts.Where(c => c.Supplier == erpSupplierName).Select(c => new ErpLinkedContact(c.Name, c.Email, c.User))]);
        }

        public Task AddPortalUserAsync(string erpSupplierName, string user, CancellationToken ct)
        {
            Before("GET Supplier portal_users");
            var rows = PortalUsers.TryGetValue(erpSupplierName, out var existing) ? existing : PortalUsers[erpSupplierName] = [];
            if (rows.Contains(user)) return Task.CompletedTask;

            Before(PortalUsersPut);
            rows.Add(user);
            After(PortalUsersPut);
            return Task.CompletedTask;
        }

        public Task SetContactUserAsync(string contactName, string user, CancellationToken ct)
        {
            Before(ContactUserPut);
            Contacts.Single(c => c.Name == contactName).User = user;
            After(ContactUserPut);
            return Task.CompletedTask;
        }

        private void Before(string call)
        {
            Calls.Add(call);
            if (FailBefore(call) is { } refusal) throw refusal;
        }

        private void After(string call)
        {
            if (FailAfter(call) is { } refusal) throw refusal;
        }

        private static ErpSupplierMatch Match(FakeSupplier supplier) => new(supplier.Name, supplier.SupplierName, supplier.TaxId);

        private static string LinkOf(JsonObject body) => body["links"]![0]!["link_name"]!.GetValue<string>();

        private static ErpFieldDefinition Field(string name, string type = "Data", params string[] options) =>
            new(name, type, options, type is "Data" or "Link" or "Select" ? 140 : null);
    }

    private sealed class RecordingJobClient : IBackgroundJobClient
    {
        public List<(Type Type, string Method, object?[] Args)> Enqueued { get; } = [];

        public string Create(Job job, IState state)
        {
            Enqueued.Add((job.Type, job.Method.Name, [.. job.Args]));
            return Guid.NewGuid().ToString();
        }

        public bool ChangeState(string jobId, IState state, string expectedState) => true;
    }

    private sealed class FixedConnection(ErpConnection? connection) : IErpConnectionProvider
    {
        public Task<ErpConnection?> CurrentAsync(CancellationToken ct) => Task.FromResult(connection);
    }

    private sealed class FixedSource(params ErpSupplier[] suppliers) : IErpSupplierSource
    {
        public Task<IReadOnlyList<ErpSupplier>> ListSuppliersAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ErpSupplier>>(suppliers);

        public Task<IReadOnlyList<string>> ListSupplierGroupsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>([]);
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
