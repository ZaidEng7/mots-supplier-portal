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
// would say nothing the first does not. The one that will need its own is the read-only service account the
// ministry's requirements ask for, and that is a different piece of work: a machine cannot answer the TOTP
// challenge this role carries today, so the feed is a browser download until it exists.
//
// IT IS AUDITED BEFORE THE FIRST BYTE, not after. A download that dies halfway still happened, and a row
// written on success would be the one missing exactly when somebody asks who took the file.

namespace MotsSupplierPortal.Api.Endpoints;

using System.Text;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Exports;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;

public static class MinistryFeedEndpoints
{
    public const string SupplierScope = "every supplier in the registry, at every onboarding state";
    public const string RfqScope = "every supplier invited to every published tender";

    public static void MapMinistryFeedEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/feeds/suppliers", async (
            IMinistrySupplierFeedHandler handler,
            IAuditLogger audit,
            HttpResponse response,
            CancellationToken ct) =>
        {
            await audit.LogAsync(
                aggregateType: "SupplierRegistry",
                aggregateId: Guid.Empty,
                action: "MinistrySupplierFeedExported",
                ct: ct);

            response.ContentType = "text/csv; charset=utf-8";
            response.Headers.ContentDisposition = $"attachment; filename={MinistrySupplierFeedCsv.FileName}";
            response.Headers["X-Feed-Generated-At"] = DateTimeOffset.UtcNow.ToString("O");
            response.Headers["X-Feed-Scope"] = SupplierScope;

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
        .WithName("ExportMinistrySupplierFeed")
        .WithTags("Feeds");

        app.MapGet("/api/v1/feeds/rfqs", async (
            IMinistryRfqFeedHandler handler,
            IAuditLogger audit,
            HttpResponse response,
            CancellationToken ct) =>
        {
            await audit.LogAsync(
                aggregateType: "Rfq",
                aggregateId: Guid.Empty,
                action: "MinistryRfqFeedExported",
                ct: ct);

            response.ContentType = "text/csv; charset=utf-8";
            response.Headers.ContentDisposition = $"attachment; filename={MinistryRfqFeedCsv.FileName}";
            response.Headers["X-Feed-Generated-At"] = DateTimeOffset.UtcNow.ToString("O");
            response.Headers["X-Feed-Scope"] = RfqScope;

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
        .WithName("ExportMinistryRfqFeed")
        .WithTags("Feeds");
    }
}
