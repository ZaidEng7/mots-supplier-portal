using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Notifications;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// SCR-901/FR-NOT-004, under D-60.
///
/// <para><b>The screen was REFUSED for two batches, and correctly</b> (D-48/D-52): FR-NOT-004 says
/// "opt-out of non-critical only" and nothing classified the 32 notification types, so building it would
/// have meant deciding - inside a preferences screen - whether a supplier may switch off the message telling
/// them they have won. D-60 made that decision and phase 1 recorded the classification. This is the screen
/// the classification was for.</para>
///
/// <para><b>The load-bearing test is the suppression one.</b> A preferences screen that stores a choice and
/// changes no delivery is the exact vacuity this project keeps finding in its own instruments - so the third
/// test below mutes a type, sends it, and requires the notification NOT to arrive, with an unmuted user in
/// the same run receiving it.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class NotificationPreferenceTests(PostgresApiFixture fixture)
{
    private const string Route = "/api/v1/notifications/preferences";

    /// <summary>Informational per D-60: another evaluator's progress, which the recipient can read off the
    /// evaluation whenever they like.</summary>
    private const string Muteable = NotificationTypes.EvaluatorSubmitted;

    /// <summary>Actionable per D-60 - an award outcome. Never muteable, and the type somebody would try
    /// first.</summary>
    private const string Actionable = NotificationTypes.AwardApproved;

    [Fact]
    public async Task Every_type_is_listed_with_whether_it_can_be_switched_off()
    {
        var (client, _) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer);

        var body = await client.GetFromJsonAsync<JsonElement>(Route);
        var types = body.GetProperty("types").EnumerateArray().ToList();

        // All 32, not only the muteable ones: saying what a user will be told REGARDLESS is half of what
        // this screen is for, and a list that omitted those rows could not render it.
        types.Should().HaveCount(NotificationTypes.All.Count);
        types.Should().OnlyContain(t => t.GetProperty("muted").GetBoolean() == false,
            "nothing is muted until somebody mutes it - absence of a row means deliver");

        var actionable = types.First(t => t.GetProperty("type").GetString() == Actionable);
        actionable.GetProperty("muteable").GetBoolean().Should().BeFalse(
            "D-60: an award outcome is delivered whatever the user prefers");

        var informational = types.First(t => t.GetProperty("type").GetString() == Muteable);
        informational.GetProperty("muteable").GetBoolean().Should().BeTrue(
            "and something must be muteable, or the screen has nothing to offer and the ruling is vacuous");
    }

    [Fact]
    public async Task An_actionable_type_is_refused_by_the_endpoint_and_not_only_by_the_screen()
    {
        var (client, userId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer);

        var refused = await client.PutAsJsonAsync(Route, new { mutedTypes = new[] { Actionable } });

        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "the screen renders these as always-on, and a screen is not a boundary");
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("NOTIFICATION_NOT_MUTEABLE");
        problem.GetProperty("types").EnumerateArray().Select(t => t.GetString()).Should().Contain(Actionable);

        var unknown = await client.PutAsJsonAsync(Route, new { mutedTypes = new[] { "not.a.notification" } });
        unknown.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await unknown.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .Should().Be("UNKNOWN_NOTIFICATION_TYPES",
                "a stored row for a type nobody sends is a preference that can never be honoured and never "
                + "be seen to fail");

        // And nothing was stored by either refusal - counted for THIS user, because the suite shares one
        // database and another test's preferences are not this one's business.
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.NotificationPreferences.CountAsync(p => p.UserId == userId)).Should().Be(0);
    }

    [Fact]
    public async Task Muting_a_type_stops_it_arriving_and_leaves_everyone_else_receiving_it()
    {
        var (muter, muterId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer);
        var (_, listenerId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer);

        var saved = await muter.PutAsJsonAsync(Route, new { mutedTypes = new[] { Muteable } });
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());

        // Sent through the real materialiser - the single place a notification row is written, which is where
        // the preference is honoured. Enqueuing through the outbox and running the dispatcher would exercise
        // the same method with more moving parts and one more source of flake.
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var materialiser = scope.ServiceProvider.GetRequiredService<INotificationMaterialiser>();
            var data = new Dictionary<string, string?> { ["rfqCode"] = "RFQ-2026-000001" };

            await materialiser.MaterialiseAsync(
                new NotificationRequest(Muteable, muterId, $"pref-test-muted:{Guid.NewGuid():N}", data));
            await materialiser.MaterialiseAsync(
                new NotificationRequest(Muteable, listenerId, $"pref-test-listener:{Guid.NewGuid():N}", data));

            // The actionable one, to the same user who muted the informational type: D-60's constraint is
            // that this still arrives, and a suppression bug keyed on the USER rather than the type would
            // pass every assertion above and fail here.
            await materialiser.MaterialiseAsync(
                new NotificationRequest(Actionable, muterId, $"pref-test-actionable:{Guid.NewGuid():N}", data));
        }

        await using var readScope = fixture.Services.CreateAsyncScope();
        var db = readScope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await db.Notifications.CountAsync(n => n.RecipientUserId == muterId && n.Type == Muteable))
            .Should().Be(0, "the whole point: a stored preference that changes no delivery is decoration");
        (await db.Notifications.CountAsync(n => n.RecipientUserId == listenerId && n.Type == Muteable))
            .Should().Be(1, "one user's choice must not silence the same message for everybody else");
        (await db.Notifications.CountAsync(n => n.RecipientUserId == muterId && n.Type == Actionable))
            .Should().Be(1, "an award outcome is not muteable, so it arrives regardless");
    }

    [Fact]
    public async Task The_set_replaces_what_was_stored_so_a_type_can_be_switched_back_on()
    {
        var (client, userId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer);

        await client.PutAsJsonAsync(Route, new { mutedTypes = new[] { Muteable, NotificationTypes.AwardErpSynced } });
        await client.PutAsJsonAsync(Route, new { mutedTypes = new[] { Muteable } });

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.NotificationPreferences.Where(p => p.UserId == userId)
            .Select(p => p.NotificationType).ToListAsync();

        stored.Should().BeEquivalentTo([Muteable],
            "the command carries the whole set, so leaving a type out is how it is switched back on - a "
            + "merge would make unmuting impossible");

        // Idempotent: the same set twice changes nothing, which is what the unique index on (user, type)
        // guarantees underneath.
        var again = await client.PutAsJsonAsync(Route, new { mutedTypes = new[] { Muteable } });
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        (await db.NotificationPreferences.CountAsync(p => p.UserId == userId)).Should().Be(1);
    }

    [Fact]
    public async Task One_users_preferences_are_not_another_users()
    {
        var (first, firstId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer);
        var (second, _) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.OnboardingReviewer);

        await first.PutAsJsonAsync(Route, new { mutedTypes = new[] { Muteable } });

        var theirs = await second.GetFromJsonAsync<JsonElement>(Route);
        theirs.GetProperty("types").EnumerateArray()
            .First(t => t.GetProperty("type").GetString() == Muteable)
            .GetProperty("muted").GetBoolean()
            .Should().BeFalse("§9.2: a preference is scoped to its own user, and there is no route to anybody else's");

        var mine = await first.GetFromJsonAsync<JsonElement>(Route);
        mine.GetProperty("types").EnumerateArray()
            .First(t => t.GetProperty("type").GetString() == Muteable)
            .GetProperty("muted").GetBoolean().Should().BeTrue();

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.NotificationPreferences.CountAsync(p => p.UserId == firstId)).Should().Be(1);
    }

    [Fact]
    public async Task A_supplier_has_the_same_screen_as_the_staff_do()
    {
        // "All authenticated", per SCREEN-INVENTORY's own persona column. A supplier receives invitations and
        // award offers, so the one screen telling them what they cannot switch off is theirs too.
        var supplier = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"Prefs {Guid.NewGuid():N}"[..20]);

        var body = await supplier.GetFromJsonAsync<JsonElement>(Route);
        body.GetProperty("types").EnumerateArray().Should().HaveCount(NotificationTypes.All.Count);

        var saved = await supplier.PutAsJsonAsync(Route, new { mutedTypes = new[] { Muteable } });
        saved.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// T-037: the classification reaches the SPA, which is what lets SCR-900 group a reader's
    /// notifications into "waiting on you" and "for information".
    ///
    /// <para>Asserted on the wire rather than on the domain set, because the defect this closes was a
    /// screen that could not see the classification - the set has existed since D-60. Both values
    /// appear in one response, so a field hard-coded either way fails.</para>
    /// </summary>
    [Fact]
    public async Task A_listed_notification_says_whether_it_is_waiting_on_the_reader()
    {
        var (client, userId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer);

        // Written through the real materialiser, the one place a notification row is created.
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var materialiser = scope.ServiceProvider.GetRequiredService<INotificationMaterialiser>();
            var data = new Dictionary<string, string?> { ["rfqCode"] = "RFQ-2026-000001" };

            await materialiser.MaterialiseAsync(
                new NotificationRequest(Actionable, userId, $"classify-actionable:{Guid.NewGuid():N}", data));
            await materialiser.MaterialiseAsync(
                new NotificationRequest(Muteable, userId, $"classify-informational:{Guid.NewGuid():N}", data));
        }

        var listed = await client.GetFromJsonAsync<JsonElement>("/api/v1/notifications");
        var rows = listed.GetProperty("data").EnumerateArray().ToList();

        rows.Single(r => r.GetProperty("type").GetString() == Actionable)
            .GetProperty("isActionable").GetBoolean().Should().BeTrue();
        rows.Single(r => r.GetProperty("type").GetString() == Muteable)
            .GetProperty("isActionable").GetBoolean().Should().BeFalse(
                "a type a person may switch off is, by D-60's own definition, one that is not waiting on them");
    }
}
