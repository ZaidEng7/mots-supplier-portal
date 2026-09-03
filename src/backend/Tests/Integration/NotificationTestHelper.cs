using MotsSupplierPortal.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Suppliers;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// EPIC-15 notifications are written to the Outbox inside the state change's transaction (D-5) and
/// materialised by the dispatcher afterwards - so a test that asserts on a notification has to run
/// the dispatcher first, exactly as production does.
///
/// <para>Asserting on the ROW rather than on the Outbox message is deliberate: the outbox entry only
/// proves something was asked for. What matters to a recipient is that a notification exists,
/// addressed to them, once.</para>
/// </summary>
public static class NotificationTestHelper
{
    public static async Task DispatchAsync(PostgresApiFixture fixture)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();

        // The dispatcher is bounded per run and the suite shares a database, so a single pass is not
        // guaranteed to reach this test's rows.
        for (var attempt = 0; attempt < 50; attempt++)
        {
            await dispatcher.DispatchPendingAsync();
            if (!await db.OutboxMessages.AnyAsync(m => m.SyncStatus == OutboxSyncStatus.Pending)) return;
        }
    }

    public static async Task<List<Notification>> ForRecipientAsync(PostgresApiFixture fixture, Guid userId, string? type = null)
    {
        await DispatchAsync(fixture);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var query = db.Notifications.AsNoTracking().Where(n => n.RecipientUserId == userId);
        if (type is not null) query = query.Where(n => n.Type == type);

        return await query.OrderBy(n => n.CreatedAt).ToListAsync();
    }

    public static async Task<Guid> UserIdAsync(PostgresApiFixture fixture, string email)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.Where(u => u.Email == email).Select(u => u.Id).FirstAsync();
    }
}
