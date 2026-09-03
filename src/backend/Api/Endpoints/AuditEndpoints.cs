using System.Text;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Audit;
using MotsSupplierPortal.Application.Reporting;
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

        // FR-AUD-003's export. Same handler method family, same scope, same gate as the list above -
        // an export that applied a different scope from the list it exports is the leak that no
        // list-level test can see, because nothing about the list changes when the export is wrong.
        app.MapGet("/api/v1/suppliers/me/audit/export", async (
            HttpContext httpContext,
            IGetAuditLogHandler handler,
            HttpResponse response,
            CancellationToken ct) =>
        {
            response.ContentType = "text/csv; charset=utf-8";
            response.Headers.ContentDisposition = "attachment; filename=my-activity-trail.csv";

            await response.Body.WriteAsync(CsvFormat.Utf8Bom, ct);
            await using var writer = new StreamWriter(response.Body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            // The scope is named in the artefact rather than left implicit. A trail export that does
            // not say whose it is cannot be checked against what its reader was entitled to see -
            // and unlike the staff export, this one's scope is the whole of its meaning.
            var provenance = new ExportProvenance(
                DateTimeOffset.UtcNow,
                Scope: "one supplier's own activity trail (FR-AUD-003)",
                Filters: []);

            foreach (var line in provenance.ToCsvComments("activity trail export"))
            {
                await writer.WriteLineAsync(line);
            }

            await writer.WriteLineAsync("Id,OccurredAt,AggregateType,AggregateId,Action,FromState,ToState,ActorLabel");

            await foreach (var entry in handler.StreamOwnTrailForExportAsync(ct))
            {
                await writer.WriteLineAsync(AuditCsvRow.Format(entry));
            }

            return Results.Empty;
        })
        .RequireAuthorization()
        .WithName("ExportOwnSupplierAuditTrail")
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

            // The provenance block is FEAT-19.4's, not this endpoint's - every artefact the export
            // engine produces carries one, in whichever format it is being rendered to.
            var provenance = new ExportProvenance(
                DateTimeOffset.UtcNow,
                Scope: "all organizations (audit.read)",
                Filters:
                [
                    ExportFilterValue.Optional("aggregateType", aggregateType),
                    ExportFilterValue.Optional("action", action),
                    ExportFilterValue.OptionalId("aggregateId", aggregateIdValue),
                    ExportFilterValue.OptionalId("actorUserId", actorUserIdValue),
                    ExportFilterValue.Bound("from", fromBound),
                    ExportFilterValue.Bound("to", toBound),
                ]);

            foreach (var line in provenance.ToCsvComments("audit export"))
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
