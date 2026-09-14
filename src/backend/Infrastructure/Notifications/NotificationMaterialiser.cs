// Writing the notification row, rendering its words from the catalogue.
//
//
// THE UNIQUE KEY IS THE IDEMPOTENCY GUARANTEE, AND IT IS USED AS ONE
//
// The insert is attempted and a duplicate-key violation is swallowed, rather than checked and then written. A
// check-then-write has a race between the two, and the dispatcher can legitimately process the same message
// twice: a run interrupted after the insert but before the status update.
//
// The written data model specifies that unique key precisely so this can be decided by the database.
//
// The same event delivered twice means one row, which is the correct outcome and not an error. So it must not
// mark the outbox message failed, and must not surface to the domain action that caused it.
//
// The exception still travels with the log line. It is expected here rather than exceptional, but a
// duplicate-key violation that turns out NOT to be this index is exactly the case where the constraint name is
// the only thing that explains it.
//
//
// IT OPENS ITS OWN CONTEXT RATHER THAN USING THE CALLER'S
//
// The dispatcher is mid-loop over outbox rows it has tracked and is about to mark as sent. Writing through that
// same context would commit those status changes early, and clearing its tracker after a duplicate would DISCARD
// them, leaving rows pending forever while the dispatcher believed it had finished.
//
// Found by the outbox tests failing only in a full suite run, where a real backlog exists.
//
//
// A PERSON'S OWN PREFERENCE IS ENFORCED HERE
//
// Because this is the single place a notification row is written: every caller in the product goes through the
// outbox and then through this method.
//
// Filtering recipients at each of the forty-odd call sites would be forty chances to forget, and the one that
// forgot would be invisible: the user would simply keep receiving something they had switched off.
//
// Whether a type may be muted at all is consulted as well as the stored row, and not as a shortcut. That test is
// fail-closed for anything it does not recognise, so a stale preference row for a type that has since become
// actionable stops suppressing it. That is the direction that matters: over-delivery irritates somebody,
// under-delivery loses them a tender.
//
// The words are the administrator's override if there is one and the shipped catalogue otherwise. The
// interpolation is the same either way.

namespace MotsSupplierPortal.Infrastructure.Notifications;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MotsSupplierPortal.Application.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class NotificationMaterialiser(IServiceScopeFactory scopeFactory, ILogger<NotificationMaterialiser> logger)
    : INotificationMaterialiser
{
    public async Task MaterialiseAsync(NotificationRequest request, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (NotificationClassification.IsMuteable(request.Type)
            && await db.NotificationPreferences.AsNoTracking().AnyAsync(
                p => p.UserId == request.RecipientUserId && p.NotificationType == request.Type, ct))
        {
            logger.LogDebug("Notification {Type} suppressed for {Recipient} by their own preference",
                request.Type, request.RecipientUserId);
            return;
        }

        var data = NotificationPayload.Build(request.Data);
        var copySource = scope.ServiceProvider.GetRequiredService<INotificationCopySource>();
        var entry = await copySource.ForAsync(request.Type, ct);
        var (titleAr, titleEn, bodyAr, bodyEn) = NotificationCatalogue.Render(entry, request.Data);

        db.Notifications.Add(new Notification
        {
            Id = Guid.CreateVersion7(),
            RecipientUserId = request.RecipientUserId,
            Type = request.Type,
            Channel = NotificationChannel.InApp,
            TitleAr = titleAr,
            TitleEn = titleEn,
            BodyAr = bodyAr,
            BodyEn = bodyEn,
            DataJson = data,
            DedupeKey = request.DedupeKey,
            DeliveryStatus = NotificationDeliveryStatus.Delivered,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException duplicate) when (IsDuplicateDedupeKey(duplicate))
        {
            logger.LogDebug(duplicate, "Notification {Type} for {Recipient} already exists (dedupe key {DedupeKey})",
                request.Type, request.RecipientUserId, request.DedupeKey);
        }
    }

    private static bool IsDuplicateDedupeKey(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" } postgres
        && postgres.ConstraintName?.Contains("DedupeKey", StringComparison.OrdinalIgnoreCase) == true;
}
