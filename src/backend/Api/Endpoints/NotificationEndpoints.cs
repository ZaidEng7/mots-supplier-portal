// The notification bell: the list, the unread count, marking one or all read, and the switches for what a user
// does not want to be told about.
//
// Nothing here needs a permission beyond being signed in, deliberately. A notification is already addressed to
// exactly one person, so the row scoping is the authorisation. A permission on top would be a second gate over
// the same fact, and the kind that drifts. The preference routes live under notifications rather than under an
// administrative surface for the same reason: they are the caller's own preferences and every persona has them.
//
// Marking read requires no write precondition, which is a considered departure from the letter of the contract.
// That contract exists to stop two writers overwriting each other. Marking read is idempotent and
// single-valued, so there is no update to lose, and requiring a precondition would make opening the bell a
// read-then-write round trip. The same reasoning covers saving preferences: sending the same set twice changes
// nothing, and two people editing one user's own preferences is not a case that exists.
//
// The list is cursor-based with no page numbers at all, because an inbox is read newest-first and scrolled
// rather than jumped into at page forty.
//
// The unread-only filter is parsed from text rather than bound directly, and bound directly it failed open: an
// unreadable value arrived as nothing, which reads as no filter, so a request for the unread set answered with
// every notification the caller has. A filter that exists to narrow, returning more than was asked for, is the
// worst shape of this defect, and a longer inbox is indistinguishable from having nothing unread, so nothing
// surfaces it.
//
// The badge is a count rather than a list, because it is on every page for every persona, and shipping fifty
// rows to render a number is the kind of thing that only becomes a problem once there are fifty.
//
// Somebody else's notification and an unknown one give the same answer.
//
// Preferences are saved as a whole set replacing what was stored, rather than one switch at a time. An absent
// or empty set means deliver everything, which is the same thing a user asking for nothing to be muted means.
//
// A switch the server will not honour is named rather than silently dropped. A screen that appeared to accept
// a mute it did not apply would be worse than one that refuses, because the user would find out by missing
// something. It goes through a proper result type rather than a plain object, because the middleware reshapes
// every failure and a plain object arrives as a generic validation failure, telling the caller that something
// was wrong rather than which switch was refused.

namespace MotsSupplierPortal.Api.Endpoints;

using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Api.Errors;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Notifications;

public sealed record SetNotificationPreferencesRequest(List<string>? MutedTypes);

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/notifications").RequireAuthorization().WithTags("Notifications");

        group.MapGet("/", async (
            string? cursor,
            int? pageSize,
            string? unreadOnly,
            HttpContext httpContext,
            IListNotificationsHandler handler,
            CancellationToken ct) =>
        {
            if (!FilterValues.TryParseBoolFilter(unreadOnly, out var unreadOnlyValue, out var badUnreadOnly))
            {
                return FilterValues.InvalidFilterValue("unreadOnly", badUnreadOnly!);
            }

            var page = await handler.HandleAsync(cursor, pageSize, unreadOnlyValue, ct);
            return ListResponse.Ok(httpContext, page, pageSize);
        })
        .WithName("ListNotifications");

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
                MarkNotificationReadResult.NotFoundOrOutOfScope => Results.NotFound(),
                _ => Results.Problem(),
            };
        })
        .WithName("MarkNotificationRead");

        group.MapPost("/read-all", async (IMarkNotificationReadHandler handler, CancellationToken ct) =>
            Results.Ok(new { marked = await handler.MarkAllReadAsync(ct) }))
        .WithName("MarkAllNotificationsRead");

        group.MapGet("/preferences", async (IGetNotificationPreferencesHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .WithName("GetNotificationPreferences");

        group.MapPut("/preferences", async (
            SetNotificationPreferencesRequest request,
            ISetNotificationPreferencesHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new SetNotificationPreferencesCommand(request.MutedTypes ?? []), ct);
            return result switch
            {
                SetNotificationPreferencesResult.Success s => Results.Ok(s.Preferences),
                SetNotificationPreferencesResult.NotMuteable n => NotificationPreferenceRefusalResult.NotMuteable(n.Types),
                SetNotificationPreferencesResult.UnknownTypes u => NotificationPreferenceRefusalResult.Unknown(u.Types),
                _ => Results.Problem(),
            };
        })
        .WithName("SetNotificationPreferences");
    }
}
