using System.Text;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Reporting;
using MotsSupplierPortal.Application.Reports;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Reporting;

namespace MotsSupplierPortal.Api.Endpoints;

/// <summary>
/// FEAT-19.1/19.2 at <c>/api/v1/reports</c>, backing the <c>/bo/reports</c> route.
///
/// <para>Gated on <see cref="Permissions.ReportRead"/>, which is an invention - the IA names the
/// route and the gate, no document defines the permission. It is granted to no role by default and
/// needs a manual grant in any deployed environment.</para>
///
/// <para>Exports go through FEAT-19.4's engine, the same one the audit and comparison exports use.
/// A second export path would mean a second provenance block, a second BOM decision and a second
/// font stack, and the one that drifts is whichever is touched least.</para>
/// </summary>
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
                    // The floor below which no cycle-time figure in this file can see. Named as a
                    // filter because that is what it behaves like - an invisible lower bound on what
                    // was measurable - and because a reader who cannot see it reads a short history
                    // as a fast process.
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
                // Stated as it is, not dressed up as a scope it does not have - Supplier carries no
                // OrganizationId, so these counts are ministry-wide by construction.
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

/// <summary>The formats a report export offers, whitelisted so an unrecognised one is refused
/// rather than silently answered in the default.</summary>
public static class ReportExportFormats
{
    public const string Pdf = "pdf";
    public const string Csv = "csv";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Pdf, Csv };
}

/// <summary>
/// Renders any sectioned report to either format through FEAT-19.4's engine. Shared by both reports
/// so the two cannot drift into two provenance shapes or two BOM decisions.
/// </summary>
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

    /// <summary>
    /// A CSV of several sections. Each section's heading is written as a comment line above its own
    /// header row: one file with three tables in it is unusual, but splitting a report into three
    /// downloads loses the provenance block on two of them.
    /// </summary>
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
