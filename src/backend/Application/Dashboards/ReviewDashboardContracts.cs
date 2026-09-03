namespace MotsSupplierPortal.Application.Dashboards;

/// <summary>
/// SCR-300 / FR-DSH-002: "review queue, SLA/aging, pending info-requests, document-expiry watchlist".
/// </summary>
public sealed record ReviewDashboardDto(
    int Pending,
    int UnderReview,
    int InfoRequested,
    int Unassigned,
    /// <summary>FR-DSH-002's "workload": how many of the open cases this reviewer holds.</summary>
    int AssignedToMe,
    /// <summary>
    /// The age in DAYS of the longest-waiting open case, and a duration rather than a breach.
    ///
    /// <para>BUSINESS-PROCESSES §2 says "start review SLA timer", "pause SLA", "resume SLA timer" and
    /// never states a duration; nothing else in the documents does either. So there is no threshold
    /// to be over, and presenting one would invent a commitment nobody made. The screen shows how
    /// long the oldest case has waited and says nothing about whether that is acceptable.</para>
    /// </summary>
    int? OldestOpenCaseAgeDays,
    IReadOnlyList<ExpiringDocumentDto> ExpiryWatchlist);

/// <summary>FR-DSH-002's "document-expiry watchlist". A read over states a daily job already sets.</summary>
public sealed record ExpiringDocumentDto(
    string SupplierReferenceCode,
    string SupplierDisplayNameAr,
    string SupplierDisplayNameEn,
    string DocumentTypeCode,
    string State,
    DateOnly? ExpiryDate);

public interface IReviewDashboardHandler
{
    Task<ReviewDashboardDto> HandleAsync(CancellationToken ct);
}
