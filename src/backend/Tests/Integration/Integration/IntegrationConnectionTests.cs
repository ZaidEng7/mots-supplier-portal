// The integrations screen, against a real database.
//
// THE FIRST TEST IS THE PRECEDENCE RULE, which is the part most likely to be quietly wrong: a deployment that has
// never opened this screen must keep running from its own settings, and the moment somebody saves an address the
// row must take over. Getting that backwards means either an administrator edits a URL and nothing happens, or an
// upgrade silently repoints a working integration at nothing.
//
// THE SECOND IS THAT THE SECRET NEVER COMES BACK. There is no route that returns it, and this asserts the shape
// of the response rather than trusting that - because the way a secret leaks is somebody adding a field to a view
// model months later without knowing why it was absent.
//
// EVERY TEST PUTS THE ROW BACK WHEN IT FINISHES. The row is shared by every test class in the collection, and the
// first version of this file only reset it at the start of each of its own tests - so the last one left the ERP
// pointed at a closed port, and the next class to ask whether an ERP was configured was told yes. That is how a
// preview test that had passed for a week started failing on a change that never touched it.
//
// THE THIRD IS THAT AN EMPTY SECRET FIELD LEAVES THE STORED ONE ALONE. The screen cannot prefill it, so it
// arrives empty on every edit, and treating that as "clear it" would wipe the credential each time somebody
// corrected a typo in the address - with nothing failing until the next run.
//
// THE SWITCH THAT CREATES SUPPLIERS IN THE ERP is saved through the same route, and four things about it are asserted:
// it comes back with its group and names the person who turned it on on the trail; a save that does not mention it -
// an older screen, or a script - leaves it as it was; turning it on without a group is refused and saves nothing, the
// address included; and the connection counts the approved suppliers the push would create, which is what the screen
// asks a person to agree to. The tests that turn it on leave the connection disabled, so nothing could push even if a
// job ran, and ResetAsync turns it off again for every class that shares the row.
//
// THE ERP'S SUPPLIER GROUPS are read from the ERP itself, so here they are asserted only where no ERP answers: 503
// with no connection, 502 from a closed local port, 404 for a connection that is not the ERP's. The read and its
// filter are ErpSupplierSourceTests', against a stub. Nothing here calls a real ERP.

namespace MotsSupplierPortal.Tests.Integration.Integration;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Integration;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class IntegrationConnectionTests(PostgresApiFixture fixture) : IAsyncLifetime
{
    private const string Erp = "/api/v1/admin/integrations/erp";

    public Task InitializeAsync() => ResetAsync(fixture);

    public Task DisposeAsync() => ResetAsync(fixture);

    internal static async Task ResetAsync(PostgresApiFixture fixture)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var row = await db.IntegrationConnections.SingleAsync(c => c.Key == IntegrationConnection.ErpKey);
        row.Update(string.Empty, string.Empty, null, isEnabled: false, Guid.Empty);
        row.SetSupplierCreation(false, null, Guid.Empty);
        await db.SaveChangesAsync();
    }

    private const string Group = "Local Suppliers - SYP";

    private static readonly DateTimeOffset Parked = new(2999, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private async Task<List<MotsSupplierPortal.Domain.Audit.AuditLog>> SupplierCreationTrailAsync(Guid actorUserId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "IntegrationSupplierCreationChanged" && a.ActorUserId == actorUserId)
            .ToListAsync();
    }

    private static async Task<JsonElement> ErpRowAsync(HttpClient admin)
    {
        var listed = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/integrations");
        return listed.EnumerateArray().Single(row => row.GetProperty("key").GetString() == "erp");
    }

    // A supplier in the given states, parked far in the future so that no push anywhere in the suite finds it due, and
    // with no ExternalId, so that the import tests, which judge every linked supplier, never see it. An approved one is
    // in service unless the test says otherwise, as approval leaves it.
    private async Task SupplierAsync(
        SupplierOnboardingState onboarding,
        SupplierErpPushStatus push,
        SupplierLifecycleState? lifecycle = null,
        SupplierErpDisabledState erpState = SupplierErpDisabledState.NotDisabled)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var supplier = Supplier.Register(
            $"SUP-W-{Guid.NewGuid():N}"[..24], "مورد منتظر", $"Waiting {Guid.NewGuid():N}"[..20],
            $"RC-{Guid.NewGuid():N}"[..16], "Rana Haddad", $"waiting-{Guid.NewGuid():N}@example.com");
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        await db.Suppliers.Where(s => s.Id == supplier.Id).ExecuteUpdateAsync(set => set
            .SetProperty(s => s.OnboardingState, onboarding)
            .SetProperty(
                s => s.LifecycleState,
                lifecycle ?? (onboarding == SupplierOnboardingState.Approved
                    ? SupplierLifecycleState.Active
                    : SupplierLifecycleState.None))
            .SetProperty(s => s.ErpDisabledState, erpState)
            .SetProperty(s => s.ErpPushStatus, push)
            .SetProperty(s => s.ErpPushNextAttemptAt, Parked));
    }

    [Fact]
    public async Task A_blank_address_leaves_the_deployments_own_settings_in_charge()
    {
        await ResetAsync(fixture);
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var listed = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/integrations");
        var erp = listed.EnumerateArray().Single(row => row.GetProperty("key").GetString() == "erp");

        erp.GetProperty("source").GetString().Should().NotBe(
            "Database",
            "a deployment that has never opened this screen must keep running from its own settings");
    }

    [Fact]
    public async Task Saving_an_address_takes_over_and_never_hands_the_secret_back()
    {
        await ResetAsync(fixture);
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var response = await admin.PutAsJsonAsync(Erp, new
        {
            baseUrl = "http://erp.example:8001",
            apiKey = "the-key",
            apiSecret = "the-secret",
            isEnabled = true,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("source").GetString().Should().Be("Database");
        body.GetProperty("hasSecret").GetBoolean().Should().BeTrue();
        body.GetProperty("baseUrl").GetString().Should().Be("http://erp.example:8001");

        body.ToString().Should().NotContain(
            "the-secret",
            "nothing in this product hands a stored credential back to a screen");
    }

    [Fact]
    public async Task An_empty_secret_field_leaves_the_stored_one_alone()
    {
        await ResetAsync(fixture);
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        await admin.PutAsJsonAsync(Erp, new
        {
            baseUrl = "http://erp.example:8001",
            apiKey = "the-key",
            apiSecret = "the-secret",
            isEnabled = true,
        });

        var corrected = await admin.PutAsJsonAsync(Erp, new
        {
            baseUrl = "http://erp.example:9001",
            apiKey = "the-key",
            apiSecret = (string?)null,
            isEnabled = true,
        });

        var body = await corrected.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("baseUrl").GetString().Should().Be("http://erp.example:9001");
        body.GetProperty("hasSecret").GetBoolean().Should().BeTrue(
            "the field arrives empty on every edit, so empty cannot mean 'clear the credential'");
    }

    [Fact]
    public async Task A_caller_without_the_permission_is_refused()
    {
        var officer = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.ProcurementOfficer);

        var response = await officer.GetAsync("/api/v1/admin/integrations");

        response.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "this decides where the product sends a ministry's credential");
    }

    [Fact]
    public async Task Testing_an_unreachable_address_answers_no_rather_than_failing()
    {
        await ResetAsync(fixture);
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        await admin.PutAsJsonAsync(Erp, new
        {
            baseUrl = "http://127.0.0.1:9/nowhere",
            apiKey = "k",
            apiSecret = "s",
            isEnabled = true,
        });

        var response = await admin.PostAsync($"{Erp}/test", content: null);

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "the request to test worked; the answer is 'no', and a 502 would make a working screen look broken");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("succeeded").GetBoolean().Should().BeFalse();
        body.GetProperty("detail").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Turning_on_supplier_creation_keeps_its_group_and_the_trail_names_who_did_it()
    {
        var (admin, adminId) = await StaffTestClient.CreateWithMfaAndIdAsync(fixture, Roles.SystemAdmin);

        var response = await admin.PutAsJsonAsync(Erp, new
        {
            baseUrl = string.Empty,
            apiKey = string.Empty,
            apiSecret = (string?)null,
            isEnabled = false,
            createSuppliersInErp = true,
            defaultSupplierGroup = Group,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("createSuppliersInErp").GetBoolean().Should().BeTrue();
        body.GetProperty("defaultSupplierGroup").GetString().Should().Be(Group);

        var listed = await ErpRowAsync(admin);
        listed.GetProperty("createSuppliersInErp").GetBoolean().Should().BeTrue("the switch is read back from the row");
        listed.GetProperty("defaultSupplierGroup").GetString().Should().Be(Group);

        var change = (await SupplierCreationTrailAsync(adminId)).Should().ContainSingle(
            "turning on writes to another ministry's system is the change somebody will later ask about").Subject;
        change.FromState.Should().Be("Off");
        change.ToState.Should().Be("On");
        change.Reason.Should().Contain(Group);
    }

    [Fact]
    public async Task A_save_that_does_not_mention_supplier_creation_leaves_it_as_it_was()
    {
        var (admin, adminId) = await StaffTestClient.CreateWithMfaAndIdAsync(fixture, Roles.SystemAdmin);

        await admin.PutAsJsonAsync(Erp, new
        {
            baseUrl = string.Empty,
            apiKey = string.Empty,
            apiSecret = (string?)null,
            isEnabled = false,
            createSuppliersInErp = true,
            defaultSupplierGroup = Group,
        });

        var corrected = await admin.PutAsJsonAsync(Erp, new
        {
            baseUrl = "http://erp.example:8001",
            apiKey = "the-key",
            apiSecret = (string?)null,
            isEnabled = false,
        });

        corrected.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await corrected.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("createSuppliersInErp").GetBoolean().Should().BeTrue(
            "a caller written before the switch existed must not turn writes off by saving an address");
        body.GetProperty("defaultSupplierGroup").GetString().Should().Be(Group);

        (await SupplierCreationTrailAsync(adminId)).Should().ContainSingle(
            "a save that changes neither the switch nor the group is not a change to either");
    }

    [Fact]
    public async Task Supplier_creation_cannot_be_turned_on_without_a_group_and_the_refused_save_keeps_nothing()
    {
        var (admin, adminId) = await StaffTestClient.CreateWithMfaAndIdAsync(fixture, Roles.SystemAdmin);

        var response = await admin.PutAsJsonAsync(Erp, new
        {
            baseUrl = "http://erp.example:8001",
            apiKey = "the-key",
            apiSecret = (string?)null,
            isEnabled = false,
            createSuppliersInErp = true,
            defaultSupplierGroup = " ",
        });

        response.StatusCode.Should().Be(
            HttpStatusCode.UnprocessableEntity,
            "the ERP refuses a supplier without a group, so every create would fail on it");

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("detail").GetString().Should().Contain("supplier group");

        var erp = await ErpRowAsync(admin);
        erp.GetProperty("createSuppliersInErp").GetBoolean().Should().BeFalse();
        erp.GetProperty("baseUrl").GetString().Should().BeEmpty(
            "a refused save keeps nothing, so what the person sees afterwards is what was there before");
        (await SupplierCreationTrailAsync(adminId)).Should().BeEmpty();
    }

    [Fact]
    public async Task The_erp_connection_counts_the_approved_suppliers_waiting_to_be_created_there()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var before = (await ErpRowAsync(admin)).GetProperty("suppliersWaitingForErp").GetInt32();

        await SupplierAsync(SupplierOnboardingState.Approved, SupplierErpPushStatus.Requested);
        await SupplierAsync(SupplierOnboardingState.Approved, SupplierErpPushStatus.Linked);
        await SupplierAsync(SupplierOnboardingState.UnderReview, SupplierErpPushStatus.Requested);
        await SupplierAsync(SupplierOnboardingState.Approved, SupplierErpPushStatus.Failed);
        await SupplierAsync(SupplierOnboardingState.Approved, SupplierErpPushStatus.Created);

        (await ErpRowAsync(admin)).GetProperty("suppliersWaitingForErp").GetInt32().Should().Be(
            before + 2,
            "the count is what the push would create once the switch is on: approved, and Requested or Linked. One "
            + "sent back for review is not pushed, a failed one waits for a person, and a created one is done");
    }

    // THE COUNT LEAVES OUT A SUPPLIER A PERSON TOOK OUT OF SERVICE, because the push does. The first version counted
    // every approved supplier waiting, so the confirmation an administrator agreed to included a deactivated one, and
    // the sweep then created it in the ERP. The sync's own hold while the ERP approves a supplier is still in service.
    [Fact]
    public async Task The_waiting_count_leaves_out_suppliers_a_person_took_out_of_service()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var before = (await ErpRowAsync(admin)).GetProperty("suppliersWaitingForErp").GetInt32();

        await SupplierAsync(
            SupplierOnboardingState.Approved, SupplierErpPushStatus.Requested, SupplierLifecycleState.Suspended,
            SupplierErpDisabledState.SuspendedAsDisabled);
        await SupplierAsync(
            SupplierOnboardingState.Approved, SupplierErpPushStatus.Linked, SupplierLifecycleState.Deactivated);
        await SupplierAsync(
            SupplierOnboardingState.Approved, SupplierErpPushStatus.Linked, SupplierLifecycleState.Suspended,
            SupplierErpDisabledState.SuspendedAsPending);

        (await ErpRowAsync(admin)).GetProperty("suppliersWaitingForErp").GetInt32().Should().Be(
            before + 1,
            "a supplier a person suspended or deactivated is not pushed, so it is not waiting; one the sync holds "
            + "only while the ERP approves it still is");
    }

    [Fact]
    public async Task With_no_erp_connection_the_supplier_groups_say_so_rather_than_listing_none()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var response = await admin.GetAsync($"{Erp}/supplier-groups");

        response.StatusCode.Should().Be(
            HttpStatusCode.ServiceUnavailable,
            "an empty list would read as an ERP with no groups, and the dropdown would offer nothing without saying why");

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("detail").GetString().Should().Contain("integrations screen");
    }

    [Fact]
    public async Task An_erp_that_does_not_answer_is_reported_as_the_erps_failure_when_its_groups_are_read()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        await admin.PutAsJsonAsync(Erp, new
        {
            baseUrl = "http://127.0.0.1:9",
            apiKey = "k",
            apiSecret = "s",
            isEnabled = true,
        });

        var response = await admin.GetAsync($"{Erp}/supplier-groups");

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("detail").GetString().Should().Contain("could not be reached");
    }

    [Fact]
    public async Task Only_the_erp_connection_has_supplier_groups_and_only_an_administrator_may_read_them()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var officer = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.ProcurementOfficer);

        (await admin.GetAsync("/api/v1/admin/integrations/not-the-erp/supplier-groups"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await officer.GetAsync($"{Erp}/supplier-groups"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
