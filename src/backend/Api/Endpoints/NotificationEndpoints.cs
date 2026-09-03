using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Notifications;

namespace MotsSupplierPortal.Api.Endpoints;

/// <summary>
/// EPIC-15 / T3-14 / SCR-900. The in-app notification channel.
///
/// <para><b>No permission beyond authentication, deliberately.</b> SCREEN-INVENTORY lists SCR-900 as
/// "all authenticated" personas, and a notification is already addressed to exactly one user - the
/// authorization IS the row scope. A permission on top would be a second gate over the same fact,
/// and the kind that drifts.</para>
///
/// <para><b>No <c>If-Match</c> on marking read</b>, and that is a considered departure from §8.1's
/// letter - see the batch report. The contract exists to prevent lost updates; marking read is
/// idempotent and single-valued, so there is no update to lose, and requiring a precondition would
/// make the bell's open gesture a read-then-write round trip.</para>
/// </summary>
public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/notifications").RequireAuthorization().WithTags("Notifications");

        // §6.1 names notifications as a cursor-default collection, so there is no page mode here at
        // all - an inbox is read newest-first and scrolled, never jumped into at page 40.
        group.MapGet("/", async (
            string? cursor,
            int? pageSize,
            string? unreadOnly,
            HttpContext httpContext,
            IListNotificationsHandler handler,
            CancellationToken ct) =>
        {
            // Bound to `bool?` this filter FAILED OPEN: `?unreadOnly=maybe` arrived as null, which
            // reads as "no filter", so a request for the unread set answered with every
            // notification the caller has. A filter that exists to narrow returning MORE than asked
            // is the worst shape of this defect - and a longer inbox is indistinguishable from
            // having nothing unread, so nothing surfaces it. See FilterValues.TryParseBoolFilter.
            if (!FilterValues.TryParseBoolFilter(unreadOnly, out var unreadOnlyValue, out var badUnreadOnly))
            {
                return FilterValues.InvalidFilterValue("unreadOnly", badUnreadOnly!);
            }

            var page = await handler.HandleAsync(cursor, pageSize, unreadOnlyValue, ct);
            return ListResponse.Ok(httpContext, page, pageSize);
        })
        .WithName("ListNotifications");

        // The bell's badge. A count rather than a list because the badge is on every page of the app
        // for every persona, and shipping fifty rows to render a number is the kind of thing that
        // only shows up as a problem once there are fifty.
        group.MapGet("/unread-count", async (IUnreadNotificationCountHandler handler, CancellationToken ct) =>
            Results.Ok(new { count = await handler.HandleAsync(ct) }))
        .WithName("UnreadNotificationCount");

        group.MapPost("/{notificationId:guid}/read", async (
            Guid notificationId,
            IMarkNotificationReadHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(notificationId, ct);
            return result switch
            {
                MarkNotificationReadResult.Success s => Results.Ok(s.Notification),
                // §9.2: someone else's notification and an unknown id are the same answer.
                MarkNotificationReadResult.NotFoundOrOutOfScope => Results.NotFound(),
                _ => Results.Problem(),
            };
        })
        .WithName("MarkNotificationRead");

        group.MapPost("/read-all", async (IMarkNotificationReadHandler handler, CancellationToken ct) =>
            Results.Ok(new { marked = await handler.MarkAllReadAsync(ct) }))
        .WithName("MarkAllNotificationsRead");
    }
}
