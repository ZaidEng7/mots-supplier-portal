namespace MotsSupplierPortal.Application.Reports;

/// <summary>One counted bucket - a state and how many rows are in it.</summary>
/// <param name="Key">The enum member name. The SPA maps it through the label catalogue; it is never
/// rendered raw, and it is the key rather than a translated string so the API stays language-neutral.</param>
public sealed record ReportCountDto(string Key, int Count);

/// <summary>
/// One measured interval between two audited RFQ state transitions.
/// </summary>
/// <param name="Key">Which interval, e.g. <c>DraftToApproved</c>.</param>
/// <param name="SampleSize">How many RFQs the median is computed over. Reported alongside the value
/// because a median of two is not the same claim as a median of two hundred, and a report that shows
/// only the number invites it to be read as the latter.</param>
/// <param name="MedianHours">Median rather than mean: one RFQ left in review over a holiday moves a
/// mean by days and a median not at all, and the question these reports answer is "how long does
/// this normally take". Null when nothing has completed the interval.</param>
public sealed record CycleTimeIntervalDto(string Key, int SampleSize, decimal? MedianHours);

/// <summary>
/// FEAT-19.1: procurement reporting - RFQ volume, cycle time, and award outcomes.
/// </summary>
/// <param name="CoverageFloor">The earliest audited transition this organization has, and therefore
/// the earliest date any cycle-time figure here can be measured from.
///
/// <para><b>Why it is in the payload and not a footnote.</b> Cycle time is derived from the audit
/// log, which began recording state transitions when that logging was added - not when the product
/// started. An RFQ that moved through review before then contributes nothing, so it is silently
/// absent from every interval below. A report that omits those reads as a LOW COUNT rather than as
/// missing data, and a low count is a conclusion. Stating the floor makes the gap a fact the reader
/// can see, exactly as the export provenance names an absent filter as "(unbounded)" rather than
/// leaving the line out.</para></param>
public sealed record ProcurementReportDto(
    IReadOnlyList<ReportCountDto> RfqsByState,
    IReadOnlyList<CycleTimeIntervalDto> CycleTimes,
    IReadOnlyList<ReportCountDto> AwardsByState,
    int TotalRfqs,
    DateTimeOffset? CoverageFloor);

/// <summary>
/// FEAT-19.2: compliance reporting - supplier health and document health.
/// </summary>
/// <param name="DocumentsExpiringSoon">Documents the daily expiry job has moved to ExpiringSoon.
/// A read over state the job already maintains, not a second expiry calculation - two places
/// computing "is this expiring" would eventually disagree, and the report would be the one nobody
/// checks.</param>
public sealed record ComplianceReportDto(
    IReadOnlyList<ReportCountDto> SuppliersByLifecycleState,
    IReadOnlyList<ReportCountDto> DocumentsByState,
    int TotalSuppliers,
    int DocumentsExpiringSoon,
    int DocumentsExpired);

public interface IProcurementReportHandler
{
    /// <summary>Null when the caller has no organization - §9.2's 404 rather than an empty report,
    /// which would assert that the organization exists and has done nothing.</summary>
    Task<ProcurementReportDto?> HandleAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct);
}

public interface IComplianceReportHandler
{
    Task<ComplianceReportDto?> HandleAsync(CancellationToken ct);
}
