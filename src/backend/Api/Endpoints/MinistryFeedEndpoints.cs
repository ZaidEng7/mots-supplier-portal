// The ministry's Syria Hotels Dashboard feeds: /api/v1/feeds/suppliers and /api/v1/feeds/rfqs, feeds 1 and 4
// of the workbook they sent. Feeds 2 and 3 are the ERP's and are not here.
//
// THE FILE HAS NO PROVENANCE COMMENT LINES, and that is the difference between this and every other export in
// the product. The registry export opens with three "#" lines saying what it is and when it was taken, because
// a person finds that file in a folder months later and needs to know. This one is read by a nightly loader,
// and a loader opening a CSV takes line one as the column names: it would read "# MOTS Supplier Portal" as the
// header and everything after it as data. So line one is SupplierID,SupplierName,... and the same three facts
// go into response headers instead, where a program can reach them.
//
// X-Feed-Generated-At and X-Feed-Scope are those facts. They are not decoration: once this file is loaded into
// a warehouse, nothing in the rows says when it was taken or that it covers every onboarding state rather than
// only approved suppliers. A feed from last March read as current is a worse failure than no feed at all.
//
// IT REUSES supplier.registry.export rather than adding a permission. The disclosure is the same - the whole
// registry, with tax identifiers and named contacts - and a second permission held by the same single role
// would say nothing the first does not.
//
// A PERSON OR A KEY MAY CALL IT, which is what the FeedCaller policy on both routes says. The permission gate
// is unchanged and does the same work for both; the policy only widens which authentication schemes are tried,
// because authorization middleware authenticates the schemes a policy names and a route naming none gets the
// bearer scheme alone. Until an API key existed these were browser downloads by necessity - the role that
// holds this permission requires a second factor, which no nightly job can answer.
//
//
// TWO REPRESENTATIONS, ONE FEED. The ministry's requirements ask for JSON; the handovers so far have been CSV
// files. Both are served from the same routes, chosen by the Accept header, because the alternative - a second
// pair of URLs - is a second thing to keep in their runbook and a second thing to forget to change.
//
// The rows cannot drift apart, because each feed decides what a row means in exactly one place and the two
// representations only differ in how they write it. Feed 1 has a projection beside its CSV class for that
// reason; feed 4 needs none, because its record already arrives as the values themselves.
//
// IT IS AUDITED BEFORE THE FIRST BYTE, not after. A download that dies halfway still happened, and a row
// written on success would be the one missing exactly when somebody asks who took the file.
//
// THE AUDIT ROW IS SAVED HERE, and that is a fix rather than a flourish. The audit logger stopped committing for
// itself deliberately - it used to commit inside its caller's transaction and turned a clean refusal into a
// server error - which left every caller responsible for its own save. These export routes have no transaction
// of their own, so they were left calling the logger and never persisting it: the row was added to the change
// tracker and dropped when the request ended. Nothing failed, nothing logged, and the table held no record of a
// single export for the life of the feature. A route that writes an audit row and does not save it is worse than
// one that writes none, because the code reads as though the trail exists.

namespace MotsSupplierPortal.Api.Endpoints;

using System.Text;
using Microsoft.AspNetCore.Mvc;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Exports;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

public static class MinistryFeedEndpoints
{
    public const string SupplierScope = "every supplier in the registry, at every onboarding state";
    public const string RfqScope = "every supplier invited to every published tender";

    // Which representation the caller asked for. CSV is what answers when nobody says, because that is what the
    // ministry has been handed by file and what a browser asks for by default - a person clicking a link must
    // not suddenly receive JSON.
    //
    // Only an explicit application/json wins, and */* does not count. A browser sends */* at the end of its
    // Accept header, so treating it as agreement would flip the default for every human caller while looking
    // like content negotiation.
    private static bool WantsJson(HttpRequest request) =>
        request.Headers.Accept.Any(value =>
            value is not null && value.Contains("application/json", StringComparison.OrdinalIgnoreCase));

    public static void MapMinistryFeedEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/feeds/suppliers", async (
            IMinistrySupplierFeedHandler handler,
            IAuditLogger audit,
            AppDbContext db,
            HttpRequest request,
            HttpResponse response,
            CancellationToken ct,
            string? cursor = null,
            int? limit = null,
            [FromQuery(Name = "modified_since")] string? modifiedSince = null) =>
        {
            await audit.LogAsync(
                aggregateType: "SupplierRegistry",
                aggregateId: Guid.Empty,
                action: "MinistrySupplierFeedExported",
                ct: ct);

            await db.SaveChangesAsync(ct);

            response.Headers["X-Feed-Generated-At"] = DateTimeOffset.UtcNow.ToString("O");
            response.Headers["X-Feed-Scope"] = SupplierScope;

            if (WantsJson(request))
            {
                string[] after = [];
                if (cursor is not null && !FeedCursor.TryDecode(cursor, 1, out after))
                {
                    return Results.Problem(
                        title: "The cursor is not one this feed issued.",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                if (!FeedModifiedSince.TryParse(modifiedSince, out var since))
                {
                    return Results.Problem(
                        title: "modified_since must be an ISO 8601 timestamp, for example 2026-09-21T10:15:00Z.",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                var size = FeedPage.ClampLimit(limit);
                var page = await handler.PageAsync(
                    cursor is null ? null : after[0], since, size + 1, ct);

                var hasMore = page.Count > size;
                var rows = page.Take(size).Select(MinistrySupplierFeedJson.Row).ToList();

                return Results.Ok(ListEnvelope<MinistrySupplierFeedJsonRow>.Cursor(
                    rows,
                    hasMore,
                    rows.Count == 0 ? null : FeedCursor.Encode(rows[^1].SupplierId),
                    size,
                    sort: "SupplierID",
                    filtersApplied: since is null ? null : ["modified_since"]));
            }

            response.ContentType = "text/csv; charset=utf-8";
            response.Headers.ContentDisposition = $"attachment; filename={MinistrySupplierFeedCsv.FileName}";

            await response.Body.WriteAsync(CsvFormat.Utf8Bom, ct);
            await using var writer = new StreamWriter(
                response.Body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            await writer.WriteLineAsync(MinistrySupplierFeedCsv.Header);

            await foreach (var record in handler.StreamAsync(ct))
            {
                await writer.WriteLineAsync(MinistrySupplierFeedCsv.Row(record));
            }

            return Results.Empty;
        })
        .RequirePermission(Permissions.SupplierRegistryExport)
        .RequireAuthorization(ApiKeyAuthentication.PolicyName)
        .WithName("ExportMinistrySupplierFeed")
        .WithTags("Feeds");

        app.MapGet("/api/v1/feeds/rfqs", async (
            IMinistryRfqFeedHandler handler,
            IAuditLogger audit,
            AppDbContext db,
            HttpRequest request,
            HttpResponse response,
            CancellationToken ct,
            string? cursor = null,
            int? limit = null,
            [FromQuery(Name = "modified_since")] string? modifiedSince = null) =>
        {
            await audit.LogAsync(
                aggregateType: "Rfq",
                aggregateId: Guid.Empty,
                action: "MinistryRfqFeedExported",
                ct: ct);

            await db.SaveChangesAsync(ct);

            response.Headers["X-Feed-Generated-At"] = DateTimeOffset.UtcNow.ToString("O");
            response.Headers["X-Feed-Scope"] = RfqScope;

            if (WantsJson(request))
            {
                string[] after = [];
                if (cursor is not null && !FeedCursor.TryDecode(cursor, 2, out after))
                {
                    return Results.Problem(
                        title: "The cursor is not one this feed issued.",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                if (!FeedModifiedSince.TryParse(modifiedSince, out var since))
                {
                    return Results.Problem(
                        title: "modified_since must be an ISO 8601 timestamp, for example 2026-09-21T10:15:00Z.",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                var size = FeedPage.ClampLimit(limit);
                var page = await handler.PageAsync(
                    cursor is null ? null : after[0], cursor is null ? null : after[1], since, size + 1, ct);

                var hasMore = page.Count > size;
                var rows = page.Take(size).Select(MinistryRfqFeedJson.Row).ToList();

                return Results.Ok(ListEnvelope<MinistryRfqFeedJsonRow>.Cursor(
                    rows,
                    hasMore,
                    rows.Count == 0 ? null : FeedCursor.Encode(rows[^1].RfqNo, rows[^1].SupplierId),
                    size,
                    sort: "RFQNo,SupplierID",
                    filtersApplied: since is null ? null : ["modified_since"]));
            }

            response.ContentType = "text/csv; charset=utf-8";
            response.Headers.ContentDisposition = $"attachment; filename={MinistryRfqFeedCsv.FileName}";

            await response.Body.WriteAsync(CsvFormat.Utf8Bom, ct);
            await using var writer = new StreamWriter(
                response.Body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            await writer.WriteLineAsync(MinistryRfqFeedCsv.Header);

            await foreach (var record in handler.StreamAsync(ct))
            {
                await writer.WriteLineAsync(MinistryRfqFeedCsv.Row(record));
            }

            return Results.Empty;
        })
        .RequirePermission(Permissions.SupplierRegistryExport)
        .RequireAuthorization(ApiKeyAuthentication.PolicyName)
        .WithName("ExportMinistryRfqFeed")
        .WithTags("Feeds");
    }
}
