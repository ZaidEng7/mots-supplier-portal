// The audit trail: a supplier reading their own history, staff searching across everything, and an export of
// either.
//
//
// THE SUPPLIER'S OWN TRAIL
//
// A supplier sees their own activity. That is deliberately not gated on the audit permission, which governs
// reading other records' trails. Reading your own company's history needs only a signed-in, supplier-scoped
// session, and the handler takes the scope from the token so it cannot be pointed at anybody else.
//
// It is paged by cursor rather than page number. The cursor is opaque: hand back the one from the previous
// response, and anything else is treated as starting from the beginning rather than as an error.
//
// The order is strictly newest-first, because the trail answers what happened to me most recently. No
// alternative order is offered, so that is both the default and the only accepted one, and anything else is
// refused rather than being an order the caller silently did not get.
//
//
// THE STAFF SEARCH
//
// Staff can search the whole trail, filterable by record type, actor, action and date range, all optional and
// combinable. It is gated on the audit permission. The per-record read above stays as it is, because it is
// bounded to one record's own rows and there is nothing to filter within it that is not already the whole
// answer.
//
//
// THE THREE FILTERS THAT ARE PARSED BY HAND
//
// The count flag, the actor identifier and the date bounds are all parsed here rather than bound by the
// framework, and each has its own reason, but the shape is the same: bound directly, a value the framework
// cannot read arrives as nothing, and nothing means no filter at all.
//
// So a search narrowed to one actor answered with every actor's rows, and a malformed bound was refused as a
// malformed body, which names no field and carries no bilingual message on a request that has no body.
// Parsed here, each refusal names the field and reads the same as every other filter refusal in this API.
//
//
// THE EXPORTS
//
// Both exports use the same handler family, the same scope and the same permission as the list they export.
// An export that applied a different scope from its list is the leak no list-level test can see, because
// nothing about the list changes when the export is wrong.
//
// There is no page limit, because an export is everything the filter matches. It is streamed straight to the
// response rather than built in memory first, so its size is not bounded by how much the process can hold at
// once.
//
// The byte-order mark is written first, before anything else reaches the body, because an Arabic export
// without it is silently unreadable in the tool most people open it with.
//
// The provenance block belongs to the shared export engine rather than to this route, so every artefact it
// produces carries one in whichever format it is rendering.
//
// The supplier export names whose trail it is inside the file itself, rather than leaving it implicit. A trail
// export that does not say whose it is cannot be checked against what its reader was entitled to see, and
// unlike the staff export, its scope is the whole of its meaning.

namespace MotsSupplierPortal.Api.Endpoints;

using System.Text;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Audit;
using MotsSupplierPortal.Application.Reporting;
using MotsSupplierPortal.Domain.Identity;

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

        app.MapGet("/api/v1/suppliers/me/audit", async (
            string? cursor,
            int? pageSize,
            string? withCount,
            HttpContext httpContext,
            IGetAuditLogHandler handler,
            CancellationToken ct) =>
        {
            if (!FilterValues.TryParseBoolFilter(withCount, out _, out var badWithCount))
            {
                return FilterValues.InvalidFilterValue("withCount", badWithCount!);
            }

            var page = await handler.HandleOwnTrailAsync(cursor, pageSize, FilterValues.BoolOrFalse(withCount), ct);
            return ListResponse.Ok(httpContext, page, pageSize);
        })
        .RequireAuthorization()
        .WithListQuery(ListQueryPolicy.Create("-occurredAt", ["occurredAt"]))
        .WithName("GetOwnSupplierAuditTrail")
        .WithTags("Audit");

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
            if (!FilterValues.TryParseBoolFilter(withCount, out _, out var badWithCount))
            {
                return FilterValues.InvalidFilterValue("withCount", badWithCount!);
            }

            if (!FilterValues.TryParseGuidFilter(aggregateId, out var aggregateIdValue, out var badAggregateId))
            {
                return FilterValues.InvalidFilterValue("aggregateId", badAggregateId!);
            }

            if (!FilterValues.TryParseGuidFilter(actorUserId, out var actorUserIdValue, out var badActorUserId))
            {
                return FilterValues.InvalidFilterValue("actorUserId", badActorUserId!);
            }

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
            if (!FilterValues.TryParseGuidFilter(aggregateId, out var aggregateIdValue, out var badAggregateId))
            {
                return FilterValues.InvalidFilterValue("aggregateId", badAggregateId!);
            }

            if (!FilterValues.TryParseGuidFilter(actorUserId, out var actorUserIdValue, out var badActorUserId))
            {
                return FilterValues.InvalidFilterValue("actorUserId", badActorUserId!);
            }

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

            await response.Body.WriteAsync(AuditCsvRow.Utf8Bom, ct);

            await using var writer = new StreamWriter(response.Body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

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

            return Results.Empty;
        })
        .RequirePermission(Permissions.AuditRead)
        .WithName("ExportAuditLog")
        .WithTags("Audit");
    }
}
