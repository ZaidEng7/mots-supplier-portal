using System.Text;
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
            string? withCount,
            HttpContext httpContext,
            IGetAuditLogHandler handler,
            CancellationToken ct) =>
        {
            // `withCount` binds to `bool?`, so an unparseable value is refused by model binding with
            // a 400 MALFORMED_JSON - the wrong code for an unprocessable filter value on a GET with
            // no body, and one that names no field. Parsed as text so the refusal is the same
            // 422/INVALID_FILTER_VALUE every other filter value in this API earns.
            if (!FilterValues.TryParseBoolFilter(withCount, out _, out var badWithCount))
            {
                return FilterValues.InvalidFilterValue("withCount", badWithCount!);
            }

            var page = await handler.HandleOwnTrailAsync(cursor, pageSize, FilterValues.BoolOrFalse(withCount), ct);
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
            string? aggregateId,
            string? actorUserId,
            string? action,
            string? from,
            string? to,
            string? cursor,
            int? pageSize,
            string? withCount,
            HttpContext httpContext,
            IGetAuditLogHandler handler,
            CancellationToken ct) =>
        {
            // `withCount` binds to `bool?`, so an unparseable value is refused by model binding with
            // a 400 MALFORMED_JSON - the wrong code for an unprocessable filter value on a GET with
            // no body, and one that names no field. Parsed as text so the refusal is the same
            // 422/INVALID_FILTER_VALUE every other filter value in this API earns.
            if (!FilterValues.TryParseBoolFilter(withCount, out _, out var badWithCount))
            {
                return FilterValues.InvalidFilterValue("withCount", badWithCount!);
            }

            // A malformed identifier is refused for the same reason a malformed date is: bound to
            // `Guid?` it arrived as null, and a null id is an ABSENT filter, so a search narrowed to
            // one actor answered with every actor's rows. See FilterValues.TryParseGuidFilter.
            if (!FilterValues.TryParseGuidFilter(aggregateId, out var aggregateIdValue, out var badAggregateId))
            {
                return FilterValues.InvalidFilterValue("aggregateId", badAggregateId!);
            }

            if (!FilterValues.TryParseGuidFilter(actorUserId, out var actorUserIdValue, out var badActorUserId))
            {
                return FilterValues.InvalidFilterValue("actorUserId", badActorUserId!);
            }

            // A malformed bound earns a 422 naming the field, rather than binding's bare 400.
            // See FilterValues.TryParseDateBound - including the correction to why.
            if (!FilterValues.TryParseDateBound(from, out var fromBound, out var badFrom))
            {
                return FilterValues.InvalidFilterValue("from", badFrom!);
            }

            if (!FilterValues.TryParseDateBound(to, out var toBound, out var badTo))
            {
                return FilterValues.InvalidFilterValue("to", badTo!);
            }

            var filter = new AuditLogFilter(aggregateType, aggregateIdValue, actorUserIdValue, action, fromBound, toBound);
            var page = await handler.HandleFilteredAsync(filter, cursor, pageSize, FilterValues.BoolOrFalse(withCount), ct);
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
            string? aggregateId,
            string? actorUserId,
            string? action,
            string? from,
            string? to,
            IGetAuditLogHandler handler,
            HttpResponse response,
            CancellationToken ct) =>
        {
            // A malformed identifier is refused for the same reason a malformed date is: bound to
            // `Guid?` it arrived as null, and a null id is an ABSENT filter, so a search narrowed to
            // one actor answered with every actor's rows. See FilterValues.TryParseGuidFilter.
            if (!FilterValues.TryParseGuidFilter(aggregateId, out var aggregateIdValue, out var badAggregateId))
            {
                return FilterValues.InvalidFilterValue("aggregateId", badAggregateId!);
            }

            if (!FilterValues.TryParseGuidFilter(actorUserId, out var actorUserIdValue, out var badActorUserId))
            {
                return FilterValues.InvalidFilterValue("actorUserId", badActorUserId!);
            }

            // The export answers a malformed bound exactly as the list does. Both were already
            // refused by binding; what changes is that the refusal now names which bound and says so
            // in both languages, instead of MALFORMED_JSON on a request with no body.
            if (!FilterValues.TryParseDateBound(from, out var fromBound, out var badFrom))
            {
                return FilterValues.InvalidFilterValue("from", badFrom!);
            }

            if (!FilterValues.TryParseDateBound(to, out var toBound, out var badTo))
            {
                return FilterValues.InvalidFilterValue("to", badTo!);
            }

            var filter = new AuditLogFilter(aggregateType, aggregateIdValue, actorUserIdValue, action, fromBound, toBound);

            response.ContentType = "text/csv; charset=utf-8";
            response.Headers.ContentDisposition = "attachment; filename=audit-log-export.csv";

            // BOM first, before anything else reaches the body - see AuditCsvRow.Utf8Bom for why an
            // Arabic export without it is silently unreadable in the tool most people open it with.
            await response.Body.WriteAsync(AuditCsvRow.Utf8Bom, ct);

            await using var writer = new StreamWriter(response.Body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            foreach (var line in AuditCsvRow.ProvenanceHeader(
                DateTimeOffset.UtcNow, aggregateType, action, fromBound, toBound,
                scopeDescription: "all organizations (audit.read)"))
            {
                await writer.WriteLineAsync(line);
            }

            await writer.WriteLineAsync("Id,OccurredAt,AggregateType,AggregateId,Action,FromState,ToState,ActorLabel");

            await foreach (var entry in handler.StreamForExportAsync(filter, ct))
            {
                await writer.WriteLineAsync(AuditCsvRow.Format(entry));
            }

            // The body is already written; this only satisfies the lambda's return type.
            return Results.Empty;
        })
        .RequirePermission(Permissions.AuditRead)
        .WithName("ExportAuditLog")
        .WithTags("Audit");
    }
}
