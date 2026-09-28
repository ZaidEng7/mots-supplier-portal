// The preview route, against a deployment with no ERP configured - which is every deployment today.
//
// THE 503 IS THE CASE WORTH A TEST, and it is a test about a startup failure as much as a response. The first
// version of this feature registered the preview handler unconditionally while its client was registered only
// when Erp:Enabled was true, so the container - which validates every registration on boot - refused to start the
// API at all. The whole product, down, over an integration meant to be optional. The fix registers a stand-in
// that throws, and this route turning that throw into a 503 is the visible half of it.
//
// AN EMPTY REPORT WOULD BE THE WRONG ANSWER and that is the reason for asserting the body rather than only the
// status. "0 suppliers, nothing to import" is what an unconfigured integration looks like if the stand-in returns
// an empty list instead of throwing, and it reads like success - somebody would forward it to the ministry.
//
// THE PERMISSION IS ASSERTED SEPARATELY because this route hands back every supplier the ERP has, with their tax
// numbers and email addresses, and it is gated on the same permission as running the import for that reason.

namespace MotsSupplierPortal.Tests.Integration.Integration;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class ErpImportPreviewEndpointTests(PostgresApiFixture fixture)
{
    private const string Preview = "/api/v1/admin/erp-import/preview";

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
            "Erp:Enabled",
            "the person reading this is an administrator looking at a deployment, so the message names the "
            + "setting rather than describing the shape of the problem");
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
}
