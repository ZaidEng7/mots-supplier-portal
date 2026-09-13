// The vocabulary for the onboarding reviewer's dashboard: the queue, how long things have waited, open
// information requests, and documents about to expire.
//
// The workload figure is how many of the open cases this reviewer holds.
//
//
// AGEING IS A DURATION, NOT A BREACH
//
// The written process says to start, pause and resume a review timer, and never states a duration. Nothing
// else in the documents does either.
//
// So there is no threshold to be over, and presenting one would invent a commitment nobody made. The screen
// reports how long the oldest open case has waited and says nothing about whether that is acceptable.
//
// The expiring-documents list is a read over the states a daily job already sets, rather than a second
// calculation of what is expiring.

namespace MotsSupplierPortal.Application.Dashboards;

public sealed record ReviewDashboardDto(
    int Pending,
    int UnderReview,
    int InfoRequested,
    int Unassigned,
    int AssignedToMe,
    int? OldestOpenCaseAgeDays,
    IReadOnlyList<ExpiringDocumentDto> ExpiryWatchlist);

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
