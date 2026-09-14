// The vocabulary for the two reports: procurement volume and cycle time, and supplier and document
// health.
//
// A counted bucket is a state and how many rows are in it. The key is the state's own name rather than a
// translated label, so the API stays language-neutral and the interface maps it through its own catalogue.
//
//
// CYCLE TIME
//
// An interval is the time between two recorded state changes.
//
// The sample size travels with every median, because a median of two is not the same claim as a median of
// two hundred, and a figure shown alone invites being read as the latter.
//
// It is a median rather than an average. One tender left in review over a holiday moves an average by days
// and a median not at all, and the question these reports answer is how long this normally takes. It is
// absent when nothing has completed the interval.
//
//
// THE COVERAGE FLOOR, WHICH IS IN THE PAYLOAD FOR A REASON
//
// It is the earliest recorded state change this organization has, and therefore the earliest date any
// cycle-time figure here can measure from.
//
// Cycle time is derived from the audit trail, which began recording state changes when that recording was
// added rather than when the product started. A tender that moved through review before then contributes
// nothing and is silently absent from every interval.
//
// A report that omits those reads as a low count rather than as missing data, and a low count is a
// conclusion. Stating the floor makes the gap a fact the reader can see, exactly as an export names an
// absent filter rather than leaving the line out.
//
//
// DOCUMENT HEALTH
//
// The expiring count is a read over the state the daily expiry job already maintains, not a second
// calculation of what is expiring. Two places computing that would eventually disagree, and the report
// would be the one nobody checks.
//
//
// NO ORGANIZATION, NO REPORT
//
// The procurement read answers nothing when the caller has no organization, rather than an empty report.
// An empty report would assert that the organization exists and has done nothing.

namespace MotsSupplierPortal.Application.Reports;

public sealed record ReportCountDto(string Key, int Count);

public sealed record CycleTimeIntervalDto(string Key, int SampleSize, decimal? MedianHours);

public sealed record ProcurementReportDto(
    IReadOnlyList<ReportCountDto> RfqsByState,
    IReadOnlyList<CycleTimeIntervalDto> CycleTimes,
    IReadOnlyList<ReportCountDto> AwardsByState,
    int TotalRfqs,
    DateTimeOffset? CoverageFloor);

public sealed record ComplianceReportDto(
    IReadOnlyList<ReportCountDto> SuppliersByLifecycleState,
    IReadOnlyList<ReportCountDto> DocumentsByState,
    int TotalSuppliers,
    int DocumentsExpiringSoon,
    int DocumentsExpired);

public interface IProcurementReportHandler
{
    Task<ProcurementReportDto?> HandleAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct);
}

public interface IComplianceReportHandler
{
    Task<ComplianceReportDto?> HandleAsync(CancellationToken ct);
}
