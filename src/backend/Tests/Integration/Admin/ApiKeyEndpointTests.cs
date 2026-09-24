// The credential another system signs in with, end to end: issued by an administrator, used against a feed,
// revoked, and refused afterwards.
//
// WHY THIS IS AN INTEGRATION TEST AND NOT A UNIT ONE. The claim being made is not "the handler stores a hash" -
// it is that a key presented in a header reaches a route whose permission gate was written for people, and that
// nothing else in the pipeline objects. Everything interesting here lives between the parts: the authentication
// scheme, the policy naming it, the permission filter, and the feed. A unit test of any one of them would pass
// with the whole chain disconnected.
//
// THE FIRST TEST IS THE WHOLE POINT and the rest are the fence around it: a key works on the feed, and then
// every way it should stop working.
//
// REVOCATION IS ASSERTED THROUGH THE ROUTE RATHER THAN THE TABLE, and on the second call rather than the row's
// column, because "the column is set" is not the property anyone cares about. The property is that the next
// request fails, and a handler that cached the key on first use would satisfy the column and not the property.
//
// THE KEY IS TRIED ON A WRITE ROUTE, which is the test that says what the credential cannot do. It holds one
// read permission, so this is a 403 on permissions - and separately the route does not admit the scheme at all,
// so there are two independent reasons it fails. A test that only ever used the key where it works would leave
// the more important half unstated.
//
// THE SECRET IS ASSERTED ABSENT FROM THE LISTING, because the one operation that must be impossible here is
// reading a key back. Nothing stores the plaintext, so this cannot regress by accident - but it can regress by
// somebody adding a convenience, and this is the line that says no.
//
// THE AUDIT ROW IS CHECKED FOR ITS ACTOR KIND. Integration was a value the enum carried and nothing wrote for
// the life of this product: every feed pull recorded itself as System, the actor our own background jobs use, so
// the trail could not tell our nightly work from the ministry's dashboard reading the national registry. That is
// the distinction this asserts, and it is invisible from any single component.

namespace MotsSupplierPortal.Tests.Integration.Admin;

using System.Net;
using System.Net.Http.Headers;
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
public sealed class ApiKeyEndpointTests(PostgresApiFixture fixture)
{
    private const string SupplierFeed = "/api/v1/feeds/suppliers";

    private static HttpClient KeyClient(PostgresApiFixture fixture, string secret)
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", secret);
        return client;
    }

    private static async Task<(string Secret, Guid Id, string Prefix)> IssueAsync(HttpClient admin, int? lifetimeDays = null)
    {
        var response = await admin.PostAsJsonAsync(
            "/api/v1/admin/api-keys",
            new { name = $"Ministry dashboard {Guid.NewGuid():N}"[..28], lifetimeDays });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        return (
            body.GetProperty("secret").GetString()!,
            body.GetProperty("key").GetProperty("id").GetGuid(),
            body.GetProperty("key").GetProperty("prefix").GetString()!);
    }

    [Fact]
    public async Task A_key_issued_by_an_administrator_can_read_the_supplier_feed()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var (secret, _, _) = await IssueAsync(admin);

        using var response = await KeyClient(fixture, secret).GetAsync(SupplierFeed);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "a nightly job holding a key is the caller this whole feature exists for");
        (await response.Content.ReadAsStringAsync()).Should().Contain("SupplierID");
    }

    [Fact]
    public async Task A_revoked_key_stops_working_on_the_next_request()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var (secret, id, _) = await IssueAsync(admin);
        var caller = KeyClient(fixture, secret);

        using var before = await caller.GetAsync(SupplierFeed);
        before.StatusCode.Should().Be(HttpStatusCode.OK, "the key has to work before revoking it proves anything");

        using var revoke = await admin.PostAsJsonAsync($"/api/v1/admin/api-keys/{id}/revoke", new { });
        revoke.StatusCode.Should().Be(HttpStatusCode.OK);

        using var after = await caller.GetAsync(SupplierFeed);
        after.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a revocation that takes effect on some later request is not a revocation");
    }

    [Fact]
    public async Task An_expired_key_is_refused()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var (secret, id, _) = await IssueAsync(admin, lifetimeDays: 1);

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var key = await db.ApiKeys.FirstAsync(k => k.Id == id);
            db.Entry(key).Property(nameof(key.ExpiresAt)).CurrentValue = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        using var response = await KeyClient(fixture, secret).GetAsync(SupplierFeed);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_real_prefix_with_the_wrong_secret_is_refused()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var (secret, _, _) = await IssueAsync(admin);

        using var response = await KeyClient(fixture, $"{secret}x").GetAsync(SupplierFeed);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the prefix names a key that exists, so this is the secret being checked rather than the lookup");
    }

    [Fact]
    public async Task A_key_cannot_be_used_on_a_write_route()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var (secret, _, _) = await IssueAsync(admin);

        using var response = await KeyClient(fixture, secret)
            .PostAsJsonAsync("/api/v1/admin/api-keys", new { name = "A key minting itself a key" });

        response.StatusCode.Should().BeOneOf([HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden],
            "the credential holds one read permission and the route does not admit its scheme - "
            + "a read-only key that could mint keys would be read-only in name only");
    }

    [Fact]
    public async Task The_secret_is_returned_once_and_never_listed()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var (secret, _, prefix) = await IssueAsync(admin);

        using var list = await admin.GetAsync("/api/v1/admin/api-keys");
        list.EnsureSuccessStatusCode();
        var body = await list.Content.ReadAsStringAsync();

        body.Should().Contain(prefix, "the listing names keys by prefix");
        body.Should().NotContain(secret,
            "nothing stores the plaintext, and no route may hand it back - a key that can be read again "
            + "is one an administrator will look up instead of replacing");
    }

    [Fact]
    public async Task A_caller_without_the_permission_cannot_issue_a_key()
    {
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);

        using var response = await reviewer.PostAsJsonAsync(
            "/api/v1/admin/api-keys", new { name = "Not mine to issue" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a reviewer holds a dozen permissions and must not hold this one");
    }

    [Fact]
    public async Task A_feed_pull_by_a_key_is_audited_as_an_integration_and_stamps_the_key()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var (secret, id, prefix) = await IssueAsync(admin);

        using var response = await KeyClient(fixture, secret).GetAsync(SupplierFeed);
        response.EnsureSuccessStatusCode();

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var row = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "MinistrySupplierFeedExported" && a.ActorLabel == prefix)
            .OrderByDescending(a => a.OccurredAt)
            .FirstOrDefaultAsync();

        row.Should().NotBeNull("the pull has to be attributable to the credential that made it");
        row!.ActorKind.Should().Be(AuditActorKind.Integration,
            "System is the actor our own background jobs use, and a trail that cannot tell them apart "
            + "cannot answer who read the national registry");
        row.ActorUserId.Should().BeNull("no person made this request");

        var key = await db.ApiKeys.AsNoTracking().FirstAsync(k => k.Id == id);
        key.LastUsedAt.Should().NotBeNull("last use is what makes revoking an unaccounted-for key safe");
    }
}
