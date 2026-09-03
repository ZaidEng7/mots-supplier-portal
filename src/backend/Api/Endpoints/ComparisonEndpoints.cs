using System.Text;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Comparison;
using MotsSupplierPortal.Application.Reporting;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Reporting;

namespace MotsSupplierPortal.Api.Endpoints;

/// <summary>FEAT-12.1..12.4/FR-CMP-001..004: read-only, no request body, no query-string
/// sort/filter parameter - see GetComparisonHandler's own doc comment on why that absence is
/// itself the mitigation for "can the query be coaxed into leaking gated data".</summary>
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

        // FR-CMP-005: the export EPIC-12 deferred as priority C and flagged rather than dropped.
        //
        // Same handler, same permission, same route prefix. That is the whole two-envelope
        // argument: this endpoint has no query of its own and therefore no second place for the
        // financial gate to be wrong. It renders whatever the screen would have rendered, and where
        // the gate left a value null it prints the "not yet visible" marker rather than a zero.
        //
        // ?format is whitelisted rather than free: an unrecognised format silently falling back to
        // one the caller did not ask for is the same class of defect as a silently ignored filter.
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
            // §9.2: an RFQ outside the caller's scope is indistinguishable from one that does not
            // exist, and the export must not become the endpoint that tells them apart.
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

/// <summary>
/// The formats the comparison export offers. A named set rather than a free string so an
/// unrecognised value is refused instead of silently answered in whichever format happens to be the
/// default - the same rule every other filter value in this API follows.
/// </summary>
public static class ComparisonExportFormats
{
    public const string Pdf = "pdf";
    public const string Csv = "csv";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Pdf, Csv };
}
