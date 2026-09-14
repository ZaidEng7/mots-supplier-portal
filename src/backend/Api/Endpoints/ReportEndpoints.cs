// The two reports, procurement and compliance, and their exports.
//
// They are gated on a permission that is an invention. The information architecture names the route and says
// it is gated, and no document defines the permission itself.
//
// Exports go through the same rendering engine the audit and comparison exports use. A second export path
// would mean a second provenance block, a second byte-order-mark decision and a second font stack, and the one
// that drifts is whichever gets touched least.
//
// The format is chosen from a named set, so an unrecognised one is refused rather than quietly answered in the
// default.
//
// One filter on the procurement report is a floor rather than a choice: the earliest date any cycle-time
// figure in this file can see. It is presented as a filter because that is what it behaves like, an invisible
// lower bound on what was measurable, and because a reader who cannot see it reads a short history as a fast
// process.
//
// The compliance counts are ministry-wide, and that is stated rather than dressed up as a scope it does not
// have. A supplier belongs to no buying organization, so those counts cannot be narrower by construction.
//
// A multi-section export is one file with several tables in it, each section's heading written as a comment
// line above its own header row. That is unusual for a spreadsheet, and splitting the report into three
// downloads would lose the provenance block on two of them.

namespace MotsSupplierPortal.Api.Endpoints;

using System.Text;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Exports;
using MotsSupplierPortal.Application.Reports;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Exports;

public static class ReportEndpoints
{
    public static void MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/reports").WithTags("Reports");

        group.MapGet("/procurement", async (
            string? from,
            string? to,
            IProcurementReportHandler handler,
            CancellationToken ct) =>
        {
            if (!FilterValues.TryParseDateBound(from, out var fromBound, out var badFrom))
            {
                return FilterValues.InvalidFilterValue("from", badFrom!);
            }

            if (!FilterValues.TryParseDateBound(to, out var toBound, out var badTo))
            {
                return FilterValues.InvalidFilterValue("to", badTo!);
            }

            var report = await handler.HandleAsync(fromBound, toBound, ct);
            return report is null ? Results.NotFound() : Results.Ok(report);
        })
        .RequirePermission(Permissions.ReportRead)
        .WithName("GetProcurementReport");

        group.MapGet("/compliance", async (IComplianceReportHandler handler, CancellationToken ct) =>
        {
            var report = await handler.HandleAsync(ct);
            return report is null ? Results.NotFound() : Results.Ok(report);
        })
        .RequirePermission(Permissions.ReportRead)
        .WithName("GetComplianceReport");

        group.MapGet("/procurement/export", async (
            string? from,
            string? to,
            string? format,
            HttpContext httpContext,
            IProcurementReportHandler handler,
            ReportFonts fonts,
            CancellationToken ct) =>
        {
            if (!FilterValues.TryParseDateBound(from, out var fromBound, out var badFrom))
            {
                return FilterValues.InvalidFilterValue("from", badFrom!);
            }

            if (!FilterValues.TryParseDateBound(to, out var toBound, out var badTo))
            {
                return FilterValues.InvalidFilterValue("to", badTo!);
            }

            if (!FilterValues.IsAllowed(format, ReportExportFormats.All, out var badFormat))
            {
                return FilterValues.InvalidFilterValue("format", badFormat!);
            }

            var report = await handler.HandleAsync(fromBound, toBound, ct);
            if (report is null) return Results.NotFound();

            var locale = RegistrationEndpoints.ResolveLocale(httpContext.Request.Headers.AcceptLanguage);
            var provenance = new ExportProvenance(
                DateTimeOffset.UtcNow,
                Scope: "one organization's RFQs (report.read)",
                Filters:
                [
                    ExportFilterValue.Bound("from", fromBound),
                    ExportFilterValue.Bound("to", toBound),
                    ExportFilterValue.Bound("cycleTimeCoverageFrom", report.CoverageFloor),
                ]);

            return ReportArtefact.Render(
                httpContext.Response, format, locale, fonts, provenance,
                ProcurementReportView.Title(locale), ProcurementReportView.ArtefactName(locale),
                "procurement-report", ProcurementReportView.Sections(report, locale), ct);
        })
        .RequirePermission(Permissions.ReportRead)
        .WithName("ExportProcurementReport");

        group.MapGet("/compliance/export", async (
            string? format,
            HttpContext httpContext,
            IComplianceReportHandler handler,
            ReportFonts fonts,
            CancellationToken ct) =>
        {
            if (!FilterValues.IsAllowed(format, ReportExportFormats.All, out var badFormat))
            {
                return FilterValues.InvalidFilterValue("format", badFormat!);
            }

            var report = await handler.HandleAsync(ct);
            if (report is null) return Results.NotFound();

            var locale = RegistrationEndpoints.ResolveLocale(httpContext.Request.Headers.AcceptLanguage);
            var provenance = new ExportProvenance(
                DateTimeOffset.UtcNow,
                Scope: "all suppliers - the registry has no organization dimension (report.read)",
                Filters: []);

            return ReportArtefact.Render(
                httpContext.Response, format, locale, fonts, provenance,
                ComplianceReportView.Title(locale), ComplianceReportView.ArtefactName(locale),
                "compliance-report", ComplianceReportView.Sections(report, locale), ct);
        })
        .RequirePermission(Permissions.ReportRead)
        .WithName("ExportComplianceReport");
    }
}

public static class ReportExportFormats
{
    public const string Pdf = "pdf";
    public const string Csv = "csv";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Pdf, Csv };
}

internal static class ReportArtefact
{
    public static IResult Render(
        HttpResponse response, string? format, string locale, ReportFonts fonts,
        ExportProvenance provenance, string title, string artefactName, string fileStem,
        IReadOnlyList<ReportSection> sections, CancellationToken ct)
    {
        if (string.Equals(format, ReportExportFormats.Csv, StringComparison.OrdinalIgnoreCase))
        {
            return new CsvReportResult(provenance, artefactName, fileStem, sections);
        }

        var buffer = new MemoryStream();
        new PdfReportWriter(fonts).Write(buffer, new PdfReportSpec(
            locale == "en" ? RunDirection.LeftToRight : RunDirection.RightToLeft,
            title, artefactName, provenance, sections));

        return Results.File(buffer.ToArray(), "application/pdf", $"{fileStem}.pdf");
    }

    private sealed record CsvReportResult(
        ExportProvenance Provenance, string ArtefactName, string FileStem, IReadOnlyList<ReportSection> Sections) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            var response = httpContext.Response;
            response.ContentType = "text/csv; charset=utf-8";
            response.Headers.ContentDisposition = $"attachment; filename={FileStem}.csv";

            await response.Body.WriteAsync(CsvFormat.Utf8Bom, httpContext.RequestAborted);
            await using var writer = new StreamWriter(
                response.Body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            foreach (var line in Provenance.ToCsvComments(ArtefactName))
            {
                await writer.WriteLineAsync(line);
            }

            foreach (var section in Sections)
            {
                await writer.WriteLineAsync();
                await writer.WriteLineAsync($"# {section.Heading}");
                await writer.WriteLineAsync(CsvFormat.Row(section.Columns));

                foreach (var row in section.Rows)
                {
                    await writer.WriteLineAsync(CsvFormat.Row(row));
                }
            }
        }
    }
}
