using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Audit;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Api.Endpoints;

public static class AuditEndpoints
{
    public static void MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/audit/{aggregateId:guid}", async (
            Guid aggregateId,
            IGetAuditLogHandler handler,
            CancellationToken ct) =>
        {
            var entries = await handler.HandleAsync(aggregateId, ct);
            return Results.Ok(entries);
        })
        .RequirePermission(Permissions.AuditRead)
        .WithName("GetAuditLog")
        .WithTags("Audit");

        // FR-AUD-003: "suppliers see their own activity trail". Intentionally NOT gated on
        // audit.read - that permission governs reading *other* aggregates' trails. Reading your
        // own supplier's history needs only an authenticated, supplier-scoped session; the handler
        // resolves the scope from the token and cannot be pointed at anyone else.
        // Keyset-paged (MSP-66, NFR-PERF-006). `cursor` is opaque - hand back the nextCursor from
        // the previous response; anything else is treated as "start from the beginning" rather than
        // as an error.
        app.MapGet("/api/v1/suppliers/me/audit", async (
            string? cursor,
            int? pageSize,
            bool? withCount,
            HttpContext httpContext,
            IGetAuditLogHandler handler,
            CancellationToken ct) =>
        {
            var page = await handler.HandleOwnTrailAsync(cursor, pageSize, withCount == true, ct);
            return ListResponse.Ok(httpContext, page, pageSize);
        })
        .RequireAuthorization()
        // §6.3: the trail is strictly reverse-chronological - "what happened to me, most recent
        // first". No alternative order is offered, so -occurredAt is both the default and the only
        // whitelisted key; anything else is a 422 rather than an order the caller silently did not get.
        .WithListQuery(ListQueryPolicy.Create("-occurredAt", ["occurredAt"]))
        .WithName("GetOwnSupplierAuditTrail")
        .WithTags("Audit");

        // MSP-75/FR-AUD-004: staff-facing global search - filterable by entity, actor, action, and
        // date range, all optional and combinable. Gated by the same audit.read permission as the
        // per-aggregate read above; that endpoint stays as-is (bounded to one aggregate's own rows,
        // nothing to filter within it that isn't already the whole answer).
        app.MapGet("/api/v1/audit", async (
            string? aggregateType,
            Guid? aggregateId,
            Guid? actorUserId,
            string? action,
            DateTimeOffset? from,
            DateTimeOffset? to,
            string? cursor,
            int? pageSize,
            bool? withCount,
            HttpContext httpContext,
            IGetAuditLogHandler handler,
            CancellationToken ct) =>
        {
            var filter = new AuditLogFilter(aggregateType, aggregateId, actorUserId, action, from, to);
            var page = await handler.HandleFilteredAsync(filter, cursor, pageSize, withCount == true, ct);
            return ListResponse.Ok(httpContext, page, pageSize);
        })
        .RequirePermission(Permissions.AuditRead)
        .WithListQuery(ListQueryPolicy.Create("-occurredAt", ["occurredAt"],
            "aggregateType", "aggregateId", "actorUserId", "action", "from", "to"))
        .WithName("SearchAuditLog")
        .WithTags("Audit");

        // Same filter, same permission, no page limit - an export is "everything the filter
        // matches". Streamed straight to the response body (StreamForExportAsync) rather than built
        // in memory first, so an export is not bounded by how much the process can hold at once.
        app.MapGet("/api/v1/audit/export", async (
            string? aggregateType,
            Guid? aggregateId,
            Guid? actorUserId,
            string? action,
            DateTimeOffset? from,
            DateTimeOffset? to,
            IGetAuditLogHandler handler,
            HttpResponse response,
            CancellationToken ct) =>
        {
            var filter = new AuditLogFilter(aggregateType, aggregateId, actorUserId, action, from, to);

            response.ContentType = "text/csv";
            response.Headers.ContentDisposition = "attachment; filename=audit-log-export.csv";

            await using var writer = new StreamWriter(response.Body);
            await writer.WriteLineAsync("Id,OccurredAt,AggregateType,AggregateId,Action,FromState,ToState,ActorLabel");

            await foreach (var entry in handler.StreamForExportAsync(filter, ct))
            {
                await writer.WriteLineAsync(AuditCsvRow.Format(entry));
            }
        })
        .RequirePermission(Permissions.AuditRead)
        .WithName("ExportAuditLog")
        .WithTags("Audit");
    }
}
