using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MotsSupplierPortal.Application.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Notifications;

/// <summary>
/// Writes the notification row, rendering its words from the catalogue.
///
/// <para><b>The unique dedupe_key is the idempotency guarantee, and it is used as one.</b> The
/// insert is attempted and a unique violation is swallowed, rather than checked-then-written: a
/// check-then-write has a race between the two, and the dispatcher can legitimately process the same
/// message twice (a run interrupted after the insert but before the status update). DATABASE-MODEL
/// §2.7 specifies U(dedupe_key) precisely so this can be decided by the database.</para>
/// </summary>
/// <para><b>Its own DbContext, not the caller's.</b> The dispatcher is mid-loop over outbox rows it
/// has tracked and is about to mark Sent; writing through that same context would commit those
/// status changes early, and clearing its tracker after a duplicate would DISCARD them - leaving
/// rows Pending forever while the dispatcher believed it had finished. Found by the outbox tests
/// failing only in a full suite run, where a real backlog exists.</para>
public sealed class NotificationMaterialiser(IServiceScopeFactory scopeFactory, ILogger<NotificationMaterialiser> logger)
    : INotificationMaterialiser
{
    public async Task MaterialiseAsync(NotificationRequest request, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // SCR-901/D-60, enforced HERE because this is the single place a notification row is written - every
        // caller in the product goes through the outbox and then through this method. Filtering recipients at
        // each of the forty-odd call sites would be forty chances to forget, and the one that forgot would be
        // invisible: the user would simply keep receiving something they had switched off.
        //
        // IsMuteable is consulted as well as the stored row, and not as a shortcut. It is fail-closed for
        // anything it does not recognise, so a stale preference row for a type that has since become
        // actionable stops suppressing it - which is the direction that matters: over-delivery irritates
        // somebody, under-delivery loses them a tender.
        if (NotificationClassification.IsMuteable(request.Type)
            && await db.NotificationPreferences.AsNoTracking().AnyAsync(
                p => p.UserId == request.RecipientUserId && p.NotificationType == request.Type, ct))
        {
            logger.LogDebug("Notification {Type} suppressed for {Recipient} by their own preference",
                request.Type, request.RecipientUserId);
            return;
        }

        var data = NotificationPayload.Build(request.Data);
        // T-061: the administrator's override if there is one, the shipped catalogue otherwise. The
        // interpolation is the same either way - an override gains no capability the shipped copy
        // lacks, and the token set it may use is the shipped copy's own.
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
            // The same event, delivered twice. One row is the correct outcome, and this is not an
            // error - so it must not mark the outbox message Failed, and must not surface to the
            // domain action that caused it (BRULE-099).
            // The exception travels with the log line. It is expected here rather than exceptional,
            // but a duplicate-key violation that turns out NOT to be the dedupe index is exactly the
            // case where the constraint name in the exception is the only thing that explains it.
            logger.LogDebug(duplicate, "Notification {Type} for {Recipient} already exists (dedupe key {DedupeKey})",
                request.Type, request.RecipientUserId, request.DedupeKey);
        }
    }

    private static bool IsDuplicateDedupeKey(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" } postgres
        && postgres.ConstraintName?.Contains("DedupeKey", StringComparison.OrdinalIgnoreCase) == true;
}
