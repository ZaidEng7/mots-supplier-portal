using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// UX-WRITING.md §10: "Never leaks data across scope (RBAC §6): suppliers see only their own".
///
/// <para>A notification centre is a NEW read surface over cross-aggregate data - it can show a
/// supplier something about an RFQ they were never invited to, or an award they are not party to,
/// purely because a row exists. The scope key is <c>recipient_user_id</c>, and these tests exist to
/// prove that it is enforced in the query rather than assumed by the caller.</para>
///
/// <para>Per §9.2, out-of-scope is <b>404</b> and not 403: someone else's notification must be
/// indistinguishable from an id that never existed, since the id is the only thing being probed.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class NotificationScopeTests(PostgresApiFixture fixture)
{
    private async Task<Guid> SeedNotificationAsync(Guid recipientUserId, string suffix)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var notification = new Notification
        {
            Id = Guid.CreateVersion7(),
            RecipientUserId = recipientUserId,
            Type = NotificationTypes.RfqApproved,
            TitleAr = "عنوان", TitleEn = "Title",
            BodyAr = "نص", BodyEn = "Body",
            DedupeKey = $"scope-test:{suffix}:{Guid.NewGuid():N}",
        };

        db.Notifications.Add(notification);
        await db.SaveChangesAsync();
        return notification.Id;
    }

    [Fact]
    public async Task A_user_reads_only_their_own_notifications()
    {
        var (clientA, userA) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer);
        var (_, userB) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer);

        var mine = await SeedNotificationAsync(userA, "mine");
        var theirs = await SeedNotificationAsync(userB, "theirs");

        var body = await clientA.GetFromJsonAsync<JsonElement>("/api/v1/notifications");
        var ids = body.GetProperty("data").EnumerateArray().Select(n => n.GetProperty("id").GetGuid()).ToList();

        // The control: the list really does return this user's own row, so the negative below cannot
        // pass because the endpoint is broken or the seed never landed.
        ids.Should().Contain(mine, "control: the owner sees their own notification");
        ids.Should().NotContain(theirs, "§10: a notification addressed to someone else must never appear");
    }

    [Fact]
    public async Task A_user_cannot_mark_someone_elses_notification_read_and_cannot_tell_it_apart_from_a_missing_one()
    {
        var (clientA, userA) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer);
        var (_, userB) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer);

        var mine = await SeedNotificationAsync(userA, "own-mark");
        var theirs = await SeedNotificationAsync(userB, "other-mark");

        // The control first: marking my own read works, so a 404 below means scoping and not a
        // broken route.
        var own = await clientA.PostAsync($"/api/v1/notifications/{mine}/read", null);
        own.StatusCode.Should().Be(HttpStatusCode.OK, "control: the owner can mark their own read");

        var other = await clientA.PostAsync($"/api/v1/notifications/{theirs}/read", null);
        var unknown = await clientA.PostAsync($"/api/v1/notifications/{Guid.NewGuid()}/read", null);

        other.StatusCode.Should().Be(HttpStatusCode.NotFound, "§9.2: out of scope reads as not-found");
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        // Field by field, not byte for byte. §7's base shape carries a per-request traceId,
        // correlationId and instance, so two responses are never byte-identical - and cannot be,
        // which is why those members carry no information about the resource either. What must
        // match is everything that could discriminate.
        static async Task<(string?, string?, int, string?)> ShapeOf(HttpResponseMessage response)
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            return (body.GetProperty("type").GetString(), body.GetProperty("title").GetString(),
                body.GetProperty("status").GetInt32(), body.GetProperty("code").GetString());
        }

        (await ShapeOf(other)).Should().Be(await ShapeOf(unknown),
            "§9.2: the two answers must be indistinguishable - the id is what is being probed");

        // And it really was not marked: a 404 that quietly performed the write would be worse than
        // a 403, because nothing would show it happened.
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var untouched = await db.Notifications.FindAsync(theirs);
        untouched!.ReadAt.Should().BeNull("the refused write must not have happened anyway");
    }

    [Fact]
    public async Task Mark_all_read_only_touches_the_callers_own()
    {
        var (clientA, userA) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer);
        var (_, userB) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer);

        var mine = await SeedNotificationAsync(userA, "bulk-mine");
        var theirs = await SeedNotificationAsync(userB, "bulk-theirs");

        var response = await clientA.PostAsync("/api/v1/notifications/read-all", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await db.Notifications.FindAsync(mine))!.ReadAt.Should().NotBeNull("control: the caller's own was marked");
        (await db.Notifications.FindAsync(theirs))!.ReadAt.Should().BeNull("a bulk action is still row-scoped");
    }

    [Fact]
    public async Task The_unread_count_is_the_callers_own()
    {
        var (clientA, userA) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer);
        var (_, userB) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer);

        await SeedNotificationAsync(userA, "count-mine");
        await SeedNotificationAsync(userB, "count-theirs-1");
        await SeedNotificationAsync(userB, "count-theirs-2");

        var body = await clientA.GetFromJsonAsync<JsonElement>("/api/v1/notifications/unread-count");

        body.GetProperty("count").GetInt32().Should().Be(1,
            "the badge counts this user's unread notifications, not the table's");
    }

    [Fact]
    public async Task An_anonymous_caller_gets_nothing()
    {
        var anonymous = fixture.CreateClient();

        var response = await anonymous.GetAsync("/api/v1/notifications");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
