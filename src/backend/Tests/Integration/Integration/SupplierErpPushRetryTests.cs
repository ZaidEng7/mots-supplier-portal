// A person starting again a push to the ERP that failed, and the reviewer's view of how far the push has got.
//
// THE SYSTEM ADMINISTRATOR IS THE CASE WORTH A TEST. They belong to no organisation, so a retry that looked the
// supplier up through the caller's organisation would answer not found to exactly the person the permission is for.
// The retry here is pressed by one, through the real route, and the trail must name them rather than the system.
//
// THE PERMISSION IS admin.integrations.manage, NOT A REVIEWER'S AND NOT integration.retry. A reviewer reads the push's
// state on the same page and may not start it again: that is a decision about the ERP connection, whose settings the
// same permission guards. integration.retry retries an award's send within the caller's organisation when they have
// one, and across the registry only for the platform administrator, who also holds admin.integrations.manage; a
// deployment may grant it to an organisation's role. The push acts on the whole registry, so a role holding only
// integration.retry is refused, which is tested by granting it to one.
//
// A SUPPLIER OUT OF SERVICE IS NOT RETRIED. One a person suspended or deactivated is not created in the ERP, so a
// retry of its failed push is refused with the domain's sentence.
//
// A PUSH THAT HAS NOT FAILED IS NOT RETRIED. Pressed twice, or on a push the job is still working on, the second is
// refused with the domain's sentence and leaves no row on the trail.
//
// THE ENQUEUED PUSH IS ASSERTED THROUGH THE HANDLER, with a recording job client in place of the scheduler, as the push
// tests assert approval's. Through the route the scheduler is real and runs nothing in this host.
//
// THE SUPPLIERS ARE FORCED INTO A FAILED PUSH rather than driven through approval and eight failed attempts, which the
// push tests already do. Each has no ExternalId, so the import tests, which judge every linked supplier, never see it,
// and a retried one is parked again at the end of its test, so that no push elsewhere in the suite finds it due.
//
// Nothing here calls an ERP.

namespace MotsSupplierPortal.Tests.Integration.Integration;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Integration.Erp;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Suppliers;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class SupplierErpPushRetryTests(PostgresApiFixture fixture)
{
    private const string LastError =
        "The ERP refused it with 417 ExpectationFailed: Value missing for Supplier: Supplier Group";

    private static readonly DateTimeOffset Parked = new(2999, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private async Task<(Guid Id, string ReferenceCode)> SupplierWithPushAsync(
        SupplierErpPushStatus push, SupplierLifecycleState lifecycle = SupplierLifecycleState.Active)
    {
        var referenceCode = $"SUP-R-{Guid.NewGuid():N}"[..24];

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var supplier = Supplier.Register(
            referenceCode, "مورد متعثر", $"Retry {Guid.NewGuid():N}"[..20], $"RC-{Guid.NewGuid():N}"[..16],
            "Rana Haddad", $"retry-{Guid.NewGuid():N}@example.com");
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        await db.Suppliers.Where(s => s.Id == supplier.Id).ExecuteUpdateAsync(set => set
            .SetProperty(s => s.OnboardingState, SupplierOnboardingState.Approved)
            .SetProperty(s => s.LifecycleState, lifecycle)
            .SetProperty(s => s.ErpPushStatus, push)
            .SetProperty(s => s.ErpPushRequestedAt, DateTimeOffset.UtcNow.AddHours(-5))
            .SetProperty(s => s.ErpPushAttempts, push == SupplierErpPushStatus.Failed ? 8 : 1)
            .SetProperty(s => s.ErpPushNextAttemptAt, push == SupplierErpPushStatus.Failed ? null : Parked)
            .SetProperty(s => s.ErpPushLastError, LastError));

        return (supplier.Id, referenceCode);
    }

    private async Task<Supplier> ReadAsync(Guid supplierId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Suppliers.AsNoTracking().SingleAsync(s => s.Id == supplierId);
    }

    private async Task<List<MotsSupplierPortal.Domain.Audit.AuditLog>> RetriesAsync(Guid supplierId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditLogs.AsNoTracking()
            .Where(a => a.AggregateId == supplierId && a.Action == "supplier.erp_push_retried")
            .ToListAsync();
    }

    private async Task ParkAsync(Guid supplierId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Suppliers.Where(s => s.Id == supplierId)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.ErpPushNextAttemptAt, Parked));
    }

    [Fact]
    public async Task A_system_administrator_retries_a_failed_push_and_the_trail_names_them()
    {
        var (supplierId, referenceCode) = await SupplierWithPushAsync(SupplierErpPushStatus.Failed);
        var (admin, adminId) = await StaffTestClient.CreateWithMfaAndIdAsync(fixture, Roles.SystemAdmin);
        var versionBefore = (await ReadAsync(supplierId)).RowVersion;

        var response = await admin.PostAsync($"/api/v1/review/{referenceCode}/retry-erp-push", content: null);
        await ParkAsync(supplierId);

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "the system administrator belongs to no organisation, and the retry must not look the supplier up through one");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("erpPushStatus").GetString().Should().Be("Requested");
        body.GetProperty("erpPushLastError").GetString().Should().Be(
            LastError, "the reason it failed stays on the supplier through a retry, so the screen can still say it");

        var supplier = await ReadAsync(supplierId);
        supplier.ErpPushStatus.Should().Be(SupplierErpPushStatus.Requested);
        supplier.ErpPushAttempts.Should().Be(0, "a person starting the push again starts the count again");
        supplier.RowVersion.Should().NotBe(
            versionBefore,
            "a person's change saves through the record, so a save that read the push before the retry is refused");

        var retried = (await RetriesAsync(supplierId)).Should().ContainSingle().Subject;
        retried.ActorUserId.Should().Be(adminId, "a person started it, and the trail says who");
        retried.FromState.Should().Be("Failed");
        retried.ToState.Should().Be("Requested");
        retried.Reason.Should().Contain(LastError);
        retried.ReferenceCode.Should().Be(referenceCode);
    }

    [Fact]
    public async Task A_push_that_has_not_failed_is_not_retried()
    {
        var (supplierId, referenceCode) = await SupplierWithPushAsync(SupplierErpPushStatus.Requested);
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var response = await admin.PostAsync($"/api/v1/review/{referenceCode}/retry-erp-push", content: null);

        response.StatusCode.Should().Be(
            HttpStatusCode.Conflict, "the job is still working on it, and a retry would restart its count underneath it");

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("detail").GetString().Should().Contain("only 'Failed' is valid");

        (await ReadAsync(supplierId)).ErpPushAttempts.Should().Be(1);
        (await RetriesAsync(supplierId)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_reviewer_sees_the_push_but_may_not_retry_it()
    {
        var (supplierId, referenceCode) = await SupplierWithPushAsync(SupplierErpPushStatus.Failed);
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);

        var view = await reviewer.GetFromJsonAsync<JsonElement>($"/api/v1/review/{referenceCode}");

        var erpSync = view.GetProperty("erpSync");
        erpSync.GetProperty("erpPushStatus").GetString().Should().Be("Failed");
        erpSync.GetProperty("erpPushLastError").GetString().Should().Be(LastError);
        erpSync.GetProperty("externalId").ValueKind.Should().Be(
            JsonValueKind.Null, "the ERP never had it, so there is no ERP name to show");

        var retry = await reviewer.PostAsync($"/api/v1/review/{referenceCode}/retry-erp-push", content: null);

        retry.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "starting the push again is a decision about the ERP connection, held by admin.integrations.manage");
        (await ReadAsync(supplierId)).ErpPushStatus.Should().Be(SupplierErpPushStatus.Failed);
    }

    // integration.retry IS NOT ENOUGH. The first version gated the route by it, and it is the permission a deployment
    // grants an organisation's role to retry its own awards; every such role could then restart any supplier's push in
    // the registry, including one an administrator left failed on purpose.
    [Fact]
    public async Task A_role_granted_integration_retry_for_awards_may_not_retry_a_suppliers_push()
    {
        var (supplierId, referenceCode) = await SupplierWithPushAsync(SupplierErpPushStatus.Failed);
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var roles = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/roles");
        var original = roles.GetProperty("roles").EnumerateArray()
            .Single(r => r.GetProperty("name").GetString() == Roles.ProcurementManager)
            .GetProperty("permissions").EnumerateArray()
            .Select(p => p.GetString()!)
            .ToArray();

        try
        {
            var granted = await admin.PutAsJsonAsync(
                $"/api/v1/admin/roles/{Roles.ProcurementManager}/permissions",
                new { permissions = original.Append(Permissions.IntegrationRetry).Distinct().ToArray() });
            granted.StatusCode.Should().Be(HttpStatusCode.OK);

            var manager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager);
            var retry = await manager.PostAsync($"/api/v1/review/{referenceCode}/retry-erp-push", content: null);

            retry.StatusCode.Should().Be(
                HttpStatusCode.Forbidden, "the push serves the whole registry, not the caller's organisation");
            (await ReadAsync(supplierId)).ErpPushStatus.Should().Be(SupplierErpPushStatus.Failed);
        }
        finally
        {
            var restored = await admin.PutAsJsonAsync(
                $"/api/v1/admin/roles/{Roles.ProcurementManager}/permissions", new { permissions = original });
            restored.StatusCode.Should().Be(HttpStatusCode.OK, "a role this test edited must be put back");
        }
    }

    [Theory]
    [InlineData(SupplierLifecycleState.Suspended)]
    [InlineData(SupplierLifecycleState.Deactivated)]
    public async Task A_failed_push_of_a_supplier_out_of_service_is_not_retried(SupplierLifecycleState lifecycle)
    {
        var (supplierId, referenceCode) = await SupplierWithPushAsync(SupplierErpPushStatus.Failed, lifecycle);
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var response = await admin.PostAsync($"/api/v1/review/{referenceCode}/retry-erp-push", content: null);

        response.StatusCode.Should().Be(
            HttpStatusCode.Conflict,
            "the push would create in the ERP, with a website user, a company whose access here was withdrawn");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("detail").GetString().Should().Contain("out of service").And.Contain(lifecycle.ToString());

        (await ReadAsync(supplierId)).ErpPushStatus.Should().Be(SupplierErpPushStatus.Failed);
        (await RetriesAsync(supplierId)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_supplier_that_does_not_exist_is_not_found()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var response = await admin.PostAsync("/api/v1/review/SUP-NOT-A-SUPPLIER/retry-erp-push", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_retry_enqueues_the_push_of_that_supplier_and_a_refused_one_enqueues_nothing()
    {
        var (failedId, failedCode) = await SupplierWithPushAsync(SupplierErpPushStatus.Failed);
        var (_, waitingCode) = await SupplierWithPushAsync(SupplierErpPushStatus.Requested);
        var jobs = new RecordingJobClient();

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var handler = new RetryErpPushHandler(
                scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                new TestScope(Guid.CreateVersion7()),
                scope.ServiceProvider.GetRequiredService<IAuditLogger>(),
                jobs);

            (await handler.HandleAsync(waitingCode, CancellationToken.None))
                .Should().BeOfType<RetryErpPushResult.Invalid>();
            jobs.Enqueued.Should().BeEmpty("nothing was started again, so there is nothing to push");

            (await handler.HandleAsync(failedCode, CancellationToken.None))
                .Should().BeOfType<RetryErpPushResult.Success>();
        }

        await ParkAsync(failedId);

        var push = jobs.Enqueued.Should().ContainSingle(
            "the push runs straight after the retry rather than at the next sweep").Subject;
        push.Type.Should().Be(typeof(SupplierErpPushJob));
        push.Method.Should().Be(nameof(SupplierErpPushJob.PushAsync));
        push.Args[0].Should().Be(failedId);
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

    private sealed class TestScope(Guid? userId = null) : IScopeContext
    {
        public Guid? UserId { get; } = userId;
        public Guid? SupplierId => null;
        public Guid? OrganizationId => null;
        public bool IsAuthenticated => UserId is not null;
        public bool HasPermission(string permission) => true;
    }
}
