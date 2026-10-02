// The preview route, against a deployment with no ERP configured and against an ERP that does not answer.
//
// THE 503 IS THE CASE WORTH A TEST, and it is a test about a startup failure as much as a response. The ERP client is
// registered whether or not a connection exists, and throws when asked to read without one; registering it only when
// the ERP was configured, beside a preview handler that was always registered, made the container - which validates
// every registration on boot - refuse to start the API at all, over an integration meant to be optional. This route
// turning that throw into a 503 is the visible half of it.
//
// AN EMPTY REPORT WOULD BE THE WRONG ANSWER and that is the reason for asserting the body rather than only the
// status. "0 suppliers, nothing to import" is what an unconfigured integration looks like if the client returns
// an empty list instead of throwing, and it reads like success - somebody would forward it to the ministry.
//
// IT STATES ITS OWN PRECONDITION rather than trusting the collection. "No ERP configured" is shared state now that
// the connection lives in a table, and a test that relies on every other class having tidied up is a test whose
// result depends on the order xUnit picks - which is how this one first failed.
//
// AN UNREACHABLE ERP IS A 502, NOT A 500. A server that does not answer at all - a refused connection, an address
// that no longer resolves - is the most likely failure right after somebody changes the address on the
// integrations screen. It used to escape as a 500, which tells an administrator the portal is broken.
//
// THE OPERATIONS SCREEN'S GENERIC "RUN NOW" IS REFUSED for the import, and the refusal names the screen to use.
// Started from there, the import ran as "system" under a different permission and nothing recorded who clicked.
//
// THE PERMISSION IS ASSERTED SEPARATELY because this route hands back every supplier the ERP has, with their tax
// numbers and email addresses, and it is gated on the same permission as running the import for that reason.
//
// THE PREVIEW'S AUDIT ROW NAMES THE PERSON WHO ASKED. It is saved before the ERP is read, so a preview refused for
// want of an ERP is on the trail too, which is what lets this test read it without any ERP answering.

namespace MotsSupplierPortal.Tests.Integration.Integration;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class ErpImportPreviewEndpointTests(PostgresApiFixture fixture) : IAsyncLifetime
{
    private const string Preview = "/api/v1/admin/erp-import/preview";

    public Task InitializeAsync() => IntegrationConnectionTests.ResetAsync(fixture);

    public Task DisposeAsync() => IntegrationConnectionTests.ResetAsync(fixture);

    [Fact]
    public async Task With_no_erp_configured_the_preview_says_so_rather_than_reporting_nothing_to_import()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var response = await admin.PostAsync(Preview, content: null);

        response.StatusCode.Should().Be(
            HttpStatusCode.ServiceUnavailable,
            "nothing is broken - the integration is switched off, and that is a different answer from an empty "
            + "registry");

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("detail").GetString().Should().Contain(
            "integrations screen",
            "the person reading this is an administrator, and there are now two places a connection can come "
            + "from - the message has to name the one they can change without a deployment");
    }

    [Fact]
    public async Task An_erp_that_does_not_answer_is_reported_as_the_erps_failure_not_ours()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        await admin.PutAsJsonAsync("/api/v1/admin/integrations/erp", new
        {
            baseUrl = "http://127.0.0.1:9",
            apiKey = "k",
            apiSecret = "s",
            isEnabled = true,
        });

        var response = await admin.PostAsync(Preview, content: null);

        response.StatusCode.Should().Be(
            HttpStatusCode.BadGateway,
            "a closed port is the other system not answering, and a 500 would tell the administrator the portal "
            + "is broken - on exactly the day they changed the address and most need to know otherwise");

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("detail").GetString().Should().Contain("could not be reached");
    }

    [Fact]
    public async Task A_caller_without_the_import_permission_is_refused()
    {
        var officer = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.ProcurementOfficer);

        var response = await officer.PostAsync(Preview, content: null);

        response.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "the report carries every ERP supplier's tax number and email address");
    }

    [Fact]
    public async Task The_operations_screen_cannot_start_the_supplier_import_behind_its_own_permission()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var response = await admin.PostAsync("/api/v1/admin/jobs/erp-supplier-sync/trigger", content: null);

        response.StatusCode.Should().Be(
            HttpStatusCode.Conflict,
            "started from the generic button it ran as 'system' under a different permission, naming nobody");

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("detail").GetString().Should().Contain("/back-office/erp-import");
    }

    [Fact]
    public async Task The_preview_is_on_the_audit_trail_with_the_person_who_asked_for_it()
    {
        var (admin, adminId) = await StaffTestClient.CreateWithMfaAndIdAsync(fixture, Roles.SystemAdmin);

        (await admin.PostAsync(Preview, content: null)).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = (await db.AuditLogs.AsNoTracking()
                .Where(a => a.Action == "ErpImportPreviewed" && a.ActorUserId == adminId)
                .ToListAsync())
            .Should().ContainSingle(
                "the preview reads every supplier out of another ministry's system, and the row must say on whose "
                + "authority").Subject;

        row.ActorKind.Should().Be(AuditActorKind.User);
        row.AggregateType.Should().Be("Supplier");
        row.AggregateId.Should().Be(Guid.Empty);
    }
}

