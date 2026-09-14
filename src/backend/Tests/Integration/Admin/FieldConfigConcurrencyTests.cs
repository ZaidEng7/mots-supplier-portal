// T-029's last genuine candidate. Batch 4's survey ruled out the other six: four have no update endpoint
// at all, SupplierDocument's state machine already refuses a second decision, and Clarification is a child
// of the already-versioned Rfq. This one had a live PUT and no version, so the second administrator's write
// silently won.
//
// The client is CreateRawClient rather than CreateClient: the fixture's default client attaches a CURRENT
// ETag to every request, and a handler that always sends the right version cannot observe a wrong one.
// system_admin requires MFA to obtain a session - see StaffTestClient - so CreateAsync 403s in both tests.
//
// The read is what makes the guard obtainable; without it the PUT refuses every caller, which is the
// batch-3 Offering failure. Then the three halves of §8.1: the version the read issued is accepted, the
// SAME version again is now stale and is refused rather than silently overwriting the write that just
// landed, and the refused write changed nothing. The restore takes a FRESH read for its version, because
// the writes above moved it and the restore is a write like any other.
//
// T-073: that restore is a finally rather than a last line. This row is a seeded config flag the whole
// suite shares - it decides whether a changed bank account re-opens compliance review - and a failing
// assertion used to skip the put-back entirely, leaving it flipped for every test that ran afterwards.
//
// The second test is the no-precondition half: a write with no If-Match is refused with 428.

namespace MotsSupplierPortal.Tests.Integration.Admin;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Domain.Identity;
using Xunit;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class FieldConfigConcurrencyTests(PostgresApiFixture fixture)
{
    private const string Path =
        "/api/v1/admin/field-config/" + FieldConfigCategory.ComplianceRetrigger + "/bankAccount";

    [Fact]
    public async Task The_read_issues_an_etag_the_write_accepts_and_a_stale_one_is_refused()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var raw = fixture.CreateRawClient();
        raw.DefaultRequestHeaders.Authorization = admin.DefaultRequestHeaders.Authorization;

        var read = await raw.GetAsync(Path);
        read.StatusCode.Should().Be(HttpStatusCode.OK, await read.Content.ReadAsStringAsync());
        var etag = read.Headers.ETag;
        etag.Should().NotBeNull("§8.1: the read is where an If-Match value comes from");

        var body = await read.Content.ReadFromJsonAsync<JsonElement>();
        var original = body.GetProperty("isEnabled").GetBoolean();

        try
        {
            var accepted = new HttpRequestMessage(HttpMethod.Put, Path)
            {
                Content = JsonContent.Create(new { isEnabled = !original }),
            };
            accepted.Headers.IfMatch.Add(etag!);
            var first = await raw.SendAsync(accepted);
            first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());

            var stale = new HttpRequestMessage(HttpMethod.Put, Path)
            {
                Content = JsonContent.Create(new { isEnabled = original }),
            };
            stale.Headers.IfMatch.Add(etag!);
            var second = await raw.SendAsync(stale);
            second.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed,
                "the second administrator's write must be refused, not resolved in their favour");

            var after = await raw.GetFromJsonAsync<JsonElement>(Path);
            after.GetProperty("isEnabled").GetBoolean().Should().Be(!original);
        }
        finally
        {
            var fresh = await raw.GetAsync(Path);
            var restore = new HttpRequestMessage(HttpMethod.Put, Path)
            {
                Content = JsonContent.Create(new { isEnabled = original }),
            };
            restore.Headers.IfMatch.Add(fresh.Headers.ETag!);
            (await raw.SendAsync(restore)).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task A_write_with_no_precondition_is_refused_with_428()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var raw = fixture.CreateRawClient();
        raw.DefaultRequestHeaders.Authorization = admin.DefaultRequestHeaders.Authorization;

        var response = await raw.PutAsJsonAsync(Path, new { isEnabled = true });

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired,
            "§8.1: a guarded write with no If-Match is 428, not a silent success");
    }
}
