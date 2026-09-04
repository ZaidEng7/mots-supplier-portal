using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Domain.Identity;
using Xunit;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// T-029's last genuine candidate. Batch 4's survey ruled out the other six: four have no update
/// endpoint at all, SupplierDocument's state machine already refuses a second decision, and
/// Clarification is a child of the already-versioned Rfq. This one had a live PUT and no version, so
/// the second administrator's write silently won.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class FieldConfigConcurrencyTests(PostgresApiFixture fixture)
{
    private const string Path =
        "/api/v1/admin/field-config/" + FieldConfigCategory.ComplianceRetrigger + "/bankAccount";

    [Fact]
    public async Task The_read_issues_an_etag_the_write_accepts_and_a_stale_one_is_refused()
    {
        // CreateRawClient, not CreateClient: the fixture's default client attaches a CURRENT ETag to
        // every request, and a handler that always sends the right version cannot observe a wrong one.
        // system_admin requires MFA to obtain a session (see StaffTestClient) - CreateAsync 403s.
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var raw = fixture.CreateRawClient();
        raw.DefaultRequestHeaders.Authorization = admin.DefaultRequestHeaders.Authorization;

        // The read that makes the guard obtainable. Without it the PUT below refuses every caller,
        // which is the batch-3 Offering failure.
        var read = await raw.GetAsync(Path);
        read.StatusCode.Should().Be(HttpStatusCode.OK, await read.Content.ReadAsStringAsync());
        var etag = read.Headers.ETag;
        etag.Should().NotBeNull("§8.1: the read is where an If-Match value comes from");

        var body = await read.Content.ReadFromJsonAsync<JsonElement>();
        var original = body.GetProperty("isEnabled").GetBoolean();

        // Satisfiable: the version the read issued is accepted.
        var accepted = new HttpRequestMessage(HttpMethod.Put, Path)
        {
            Content = JsonContent.Create(new { isEnabled = !original }),
        };
        accepted.Headers.IfMatch.Add(etag!);
        var first = await raw.SendAsync(accepted);
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());

        // Refusable: the SAME version again, now stale, is refused rather than silently overwriting
        // the write that just landed.
        var stale = new HttpRequestMessage(HttpMethod.Put, Path)
        {
            Content = JsonContent.Create(new { isEnabled = original }),
        };
        stale.Headers.IfMatch.Add(etag!);
        var second = await raw.SendAsync(stale);
        second.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed,
            "the second administrator's write must be refused, not resolved in their favour");

        // And the refused write changed nothing.
        var after = await raw.GetFromJsonAsync<JsonElement>(Path);
        after.GetProperty("isEnabled").GetBoolean().Should().Be(!original);

        // Put it back, so this test does not leave a global config row flipped for the suite.
        var fresh = await raw.GetAsync(Path);
        var restore = new HttpRequestMessage(HttpMethod.Put, Path)
        {
            Content = JsonContent.Create(new { isEnabled = original }),
        };
        restore.Headers.IfMatch.Add(fresh.Headers.ETag!);
        (await raw.SendAsync(restore)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_write_with_no_precondition_is_refused_with_428()
    {
        // system_admin requires MFA to obtain a session (see StaffTestClient) - CreateAsync 403s.
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var raw = fixture.CreateRawClient();
        raw.DefaultRequestHeaders.Authorization = admin.DefaultRequestHeaders.Authorization;

        var response = await raw.PutAsJsonAsync(Path, new { isEnabled = true });

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired,
            "§8.1: a guarded write with no If-Match is 428, not a silent success");
    }
}
