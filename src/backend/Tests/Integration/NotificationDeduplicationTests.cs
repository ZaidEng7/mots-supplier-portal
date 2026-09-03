using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// DATABASE-MODEL.md §2.7's <c>U(dedupe_key)</c>, tested as the idempotency guarantee it is.
///
/// <para>The same event delivered twice must produce ONE row - and the second attempt must not error
/// the domain action that caused it (BRULE-099: a notification failure never rolls back a committed
/// change). Those are two separate claims and both are asserted here.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class NotificationDeduplicationTests(PostgresApiFixture fixture)
{
    private async Task<Guid> RecipientAsync()
    {
        var (_, userId) = await StaffTestClient.CreateWithIdAsync(fixture, MotsSupplierPortal.Domain.Identity.Roles.ProcurementOfficer);
        return userId;
    }

    [Fact]
    public async Task The_same_event_delivered_twice_produces_one_row()
    {
        var recipient = await RecipientAsync();
        var dedupeKey = $"dedupe-test:{Guid.NewGuid():N}";

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Two identical outbox rows - which is exactly what a dispatcher run interrupted after the
        // insert but before the status update leaves behind.
        NotificationOutbox.Enqueue(db, NotificationTypes.RfqApproved, recipient, dedupeKey,
            new Dictionary<string, string?> { ["rfqCode"] = "RFQ-2026-000001" });
        NotificationOutbox.Enqueue(db, NotificationTypes.RfqApproved, recipient, dedupeKey,
            new Dictionary<string, string?> { ["rfqCode"] = "RFQ-2026-000001" });
        await db.SaveChangesAsync();

        await NotificationTestHelper.DispatchAsync(fixture);

        var rows = await db.Notifications.AsNoTracking()
            .Where(n => n.DedupeKey == dedupeKey).ToListAsync();

        rows.Should().ContainSingle("U(dedupe_key) is the idempotency guarantee, not a hint");
    }

    [Fact]
    public async Task A_duplicate_does_not_fail_the_dispatch_run()
    {
        // BRULE-099's shape: the second delivery is a no-op, not an error. If it marked the outbox
        // message Failed, a retry storm would follow; if it threw, the run would abandon every later
        // message in the batch.
        var recipient = await RecipientAsync();
        var dedupeKey = $"dedupe-noerror:{Guid.NewGuid():N}";

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        NotificationOutbox.Enqueue(db, NotificationTypes.RfqApproved, recipient, dedupeKey);
        NotificationOutbox.Enqueue(db, NotificationTypes.RfqApproved, recipient, dedupeKey);
        await db.SaveChangesAsync();

        await NotificationTestHelper.DispatchAsync(fixture);

        // Narrowed in SQL by type, then matched in memory: PayloadJson is a jsonb column and
        // Postgres has no LIKE for jsonb, so a Contains() here translates to an operator that does
        // not exist rather than to a scan.
        var candidates = await db.OutboxMessages.AsNoTracking()
            .Where(m => m.Type == NotificationRequest.OutboxType)
            .Select(m => new { m.PayloadJson, m.SyncStatus })
            .ToListAsync();

        var outboxRows = candidates
            .Where(m => m.PayloadJson.Contains(dedupeKey, StringComparison.Ordinal))
            .Select(m => m.SyncStatus)
            .ToList();

        outboxRows.Should().HaveCount(2);
        outboxRows.Should().AllBeEquivalentTo(MotsSupplierPortal.Domain.Common.OutboxSyncStatus.Sent,
            "a duplicate is the correct outcome of an at-least-once delivery, not a failure");
    }

    [Fact]
    public async Task Two_recipients_of_the_same_event_each_get_their_own_row()
    {
        // The control for the dedupe tests above: deduplication must not collapse a group
        // notification into one row, or only the first invitee would ever hear about it.
        var first = await RecipientAsync();
        var second = await RecipientAsync();
        var prefix = $"dedupe-group:{Guid.NewGuid():N}";

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        NotificationOutbox.EnqueueMany(db, NotificationTypes.RfqApproved, [first, second], prefix);
        await db.SaveChangesAsync();

        await NotificationTestHelper.DispatchAsync(fixture);

        var rows = await db.Notifications.AsNoTracking()
            .Where(n => n.DedupeKey.StartsWith(prefix)).ToListAsync();

        rows.Should().HaveCount(2);
        rows.Select(r => r.RecipientUserId).Should().BeEquivalentTo(new[] { first, second });
    }

    [Fact]
    public async Task The_stored_row_carries_the_catalogue_copy_in_both_languages()
    {
        var recipient = await RecipientAsync();
        var dedupeKey = $"dedupe-copy:{Guid.NewGuid():N}";

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        NotificationOutbox.Enqueue(db, NotificationTypes.RfqApproved, recipient, dedupeKey,
            new Dictionary<string, string?> { ["rfqCode"] = "RFQ-2026-000042" });
        await db.SaveChangesAsync();

        await NotificationTestHelper.DispatchAsync(fixture);

        var row = await db.Notifications.AsNoTracking().SingleAsync(n => n.DedupeKey == dedupeKey);

        row.TitleAr.Should().Be(NotificationCatalogue.For(NotificationTypes.RfqApproved).TitleAr);
        row.BodyEn.Should().Contain("RFQ-2026-000042", "the reference code is interpolated into the copy");
        row.BodyAr.Should().Contain("RFQ-2026-000042");
        row.BodyAr.Should().MatchRegex("[؀-ۿ]", "the Arabic body must be Arabic, not the English fallback");
    }
}
