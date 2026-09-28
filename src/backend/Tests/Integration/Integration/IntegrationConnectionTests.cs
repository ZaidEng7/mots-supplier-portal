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

namespace MotsSupplierPortal.Tests.Integration.Integration;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Integration;
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
        await db.SaveChangesAsync();
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
}
