// The bid comparison: one read-only screen, and its export.
//
// There is no request body and no filter or sort in the query string, and that absence is itself the
// protection. The question worth asking of a comparison endpoint is whether its query can be coaxed into
// revealing the pricing the two-envelope rule is holding back, and an endpoint with no query has nowhere for
// that to go wrong.
//
// The export is the same handler, the same permission and the same route prefix, which is the whole argument:
// it has no query of its own and therefore no second place for the financial gate to be wrong. It renders
// whatever the screen would have rendered, and where the gate left a value empty it prints a not-yet-visible
// marker rather than a zero.
//
// The format is chosen from a named set rather than a free string, so an unrecognised value is refused
// instead of quietly answered in whichever format happens to be the default. That is the same rule every
// other filter value in this API follows.
//
// A tender outside the caller's scope is indistinguishable from one that does not exist, and the export must
// not become the endpoint that tells them apart.

namespace MotsSupplierPortal.Api.Endpoints;

using System.Text;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Comparison;
using MotsSupplierPortal.Application.Exports;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Exports;

public static class ComparisonEndpoints
{
    public static void MapComparisonEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/rfqs/{referenceCode}/comparison", async (
            string referenceCode, IGetComparisonHandler handler, CancellationToken ct) =>
        {
            var comparison = await handler.HandleAsync(referenceCode, ct);
            return comparison is null ? Results.NotFound() : Results.Ok(comparison);
        })
        .RequirePermission(Permissions.ComparisonView)
        .WithTags("Comparison")
        .WithName("GetComparison");

        app.MapGet("/api/v1/rfqs/{referenceCode}/comparison/export", async (
            string referenceCode,
            string? format,
            HttpContext httpContext,
            IGetComparisonHandler handler,
            ReportFonts fonts,
            CancellationToken ct) =>
        {
            if (!FilterValues.IsAllowed(format, ComparisonExportFormats.All, out var invalidFormat))
            {
                return FilterValues.InvalidFilterValue("format", invalidFormat!);
            }

            var comparison = await handler.HandleAsync(referenceCode, ct);
            if (comparison is null) return Results.NotFound();

            var locale = RegistrationEndpoints.ResolveLocale(httpContext.Request.Headers.AcceptLanguage);
            var provenance = ComparisonExport.Provenance(
                comparison, DateTimeOffset.UtcNow,
                scope: $"comparison.view on {comparison.RfqReferenceCode}");

            return string.Equals(format, ComparisonExportFormats.Csv, StringComparison.OrdinalIgnoreCase)
                ? await WriteCsvAsync(httpContext.Response, comparison, provenance, locale, ct)
                : WritePdf(comparison, provenance, locale, fonts);
        })
        .RequirePermission(Permissions.ComparisonView)
        .WithTags("Comparison")
        .WithName("ExportComparison");
    }

    private static async Task<IResult> WriteCsvAsync(
        HttpResponse response, ComparisonDto comparison, ExportProvenance provenance, string locale, CancellationToken ct)
    {
        response.ContentType = "text/csv; charset=utf-8";
        response.Headers.ContentDisposition =
            $"attachment; filename=comparison-{comparison.RfqReferenceCode}.csv";

        await response.Body.WriteAsync(CsvFormat.Utf8Bom, ct);
        await using var writer = new StreamWriter(response.Body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        foreach (var line in provenance.ToCsvComments(ComparisonExport.ArtefactName(locale)))
        {
            await writer.WriteLineAsync(line);
        }

        await writer.WriteLineAsync(CsvFormat.Row(ComparisonExport.Columns(locale)));

        foreach (var row in ComparisonExport.Rows(comparison, locale))
        {
            await writer.WriteLineAsync(CsvFormat.Row(row));
        }

        return Results.Empty;
    }

    private static IResult WritePdf(
        ComparisonDto comparison, ExportProvenance provenance, string locale, ReportFonts fonts)
    {
        var buffer = new MemoryStream();
        new PdfReportWriter(fonts).Write(buffer, new PdfReportSpec(
            locale == "en" ? RunDirection.LeftToRight : RunDirection.RightToLeft,
            ComparisonExport.Title(comparison, locale),
            ComparisonExport.ArtefactName(locale),
            provenance,
            [new ReportSection(
                ComparisonExport.Title(comparison, locale),
                ComparisonExport.Columns(locale),
                ComparisonExport.Rows(comparison, locale))]));

        return Results.File(buffer.ToArray(), "application/pdf",
            $"comparison-{comparison.RfqReferenceCode}.pdf");
    }
}

public static class ComparisonExportFormats
{
    public const string Pdf = "pdf";
    public const string Csv = "csv";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Pdf, Csv };
}
