// GET /api/v1/suppliers/export - the supplier registry as one CSV, for the ministry data lake.
//
// It STREAMS straight to the response body rather than building a file and handing it over. The registry with
// every child collection attached is the largest thing this product exports, and buffering it would hold the
// whole file in memory per concurrent caller. The audit trail export next door works the same way, for the
// same reason.
//
// THE PROVENANCE HEADER IS THE POINT, not decoration. Once this file is detached from the request that made it
// - sitting in a lake, loaded into a dashboard, attached to an email - nothing about it says when it was taken
// or what it covers. A registry export from March read as current is a worse failure than no export at all. So
// the file states its product, its timestamp and its scope in comment lines before the header row, and the
// scope says every onboarding state out loud, because a file named suppliers that silently meant approved
// suppliers would be counted as the whole registry.
//
// THE BOM IS NOT OPTIONAL. Half the readers of this file will open it in Excel first to check it looks right,
// and without a byte-order mark Excel renders the Arabic columns as mojibake - at which point the conclusion is
// that the data is broken rather than that the viewer guessed the encoding wrong.
//
// IT IS AUDITED. Taking the entire supplier base including tax identifiers and named contacts out of the
// product is an event worth being able to point at afterwards, so it writes an audit row before the first byte
// - before, not after, because a download that dies halfway still happened and a row written on success would
// be the one record missing exactly when someone asks.
//
// THE PERMISSION IS ITS OWN. See the note beside SupplierRegistryExport in the permission catalogue: reading
// the directory a supplier at a time and carrying all of them out in one file are different disclosures, and
// the second one is held by the system administrator alone.

namespace MotsSupplierPortal.Api.Endpoints;

using System.Text;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Exports;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;

public static class SupplierExportEndpoints
{
    public const string FileName = "mots-suppliers.csv";

    public static void MapSupplierExportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/suppliers/export", async (
            ISupplierRegistryExportHandler handler,
            IAuditLogger audit,
            HttpResponse response,
            CancellationToken ct) =>
        {
            await audit.LogAsync(
                aggregateType: "SupplierRegistry",
                aggregateId: Guid.Empty,
                action: "SupplierRegistryExported",
                ct: ct);

            var lookups = await handler.GetLookupsAsync(ct);

            response.ContentType = "text/csv; charset=utf-8";
            response.Headers.ContentDisposition = $"attachment; filename={FileName}";

            await response.Body.WriteAsync(CsvFormat.Utf8Bom, ct);
            await using var writer = new StreamWriter(
                response.Body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var provenance = new ExportProvenance(
                DateTimeOffset.UtcNow,
                Scope: "every supplier in the registry, at every onboarding state (supplier.registry.export)",
                Filters: []);

            foreach (var line in provenance.ToCsvComments("supplier registry export"))
            {
                await writer.WriteLineAsync(line);
            }

            await writer.WriteLineAsync(SupplierExportCsv.Header);

            await foreach (var record in handler.StreamAsync(ct))
            {
                await writer.WriteLineAsync(SupplierExportCsv.Row(record, lookups));
            }

            return Results.Empty;
        })
        .RequirePermission(Permissions.SupplierRegistryExport)
        .WithName("ExportSupplierRegistry")
        .WithTags("Suppliers");
    }
}
