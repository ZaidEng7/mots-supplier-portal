// The shapes the reviewer's queue and their view of one application are read through.
//
//
// WHEN A CASE ENTERED THE QUEUE
//
// It is when the application most recently entered the reviewer's active queue: submitted, resubmitted, resumed
// after information was provided, or sent back by a compliance re-trigger.
//
// It is deliberately not the registration date, which would make a long-registered supplier who has just
// resubmitted read as stale.
//
// It falls back to the registration date only when no such record exists. That should not happen for a case in
// a queue-eligible state, but a row without one reads as just arrived rather than crashing the queue.
//
//
// THE REVIEW TARGET IS A TARGET
//
// Counted in working days from when the case entered the queue.
//
// Deliberately not a breach and not a badge. The written process starts, pauses and resumes a review timer and
// never names a duration, so there is no threshold to be over. Presenting one would invent a commitment nobody
// made.

namespace MotsSupplierPortal.Application.Suppliers;

using MotsSupplierPortal.Application.Common;

public sealed record ReviewQueueItemDto(
    string ReferenceCode, string DisplayNameAr, string DisplayNameEn, string OnboardingState, DateTimeOffset EnteredQueueAt,
    Guid? AssignedReviewerId, string? AssignedReviewerName,
    DateTimeOffset? ReviewTargetAt = null);

public static class ReviewQueueFilterValues
{
    public static readonly IReadOnlySet<string> States =
        new HashSet<string>(StringComparer.Ordinal) { "Submitted", "UnderReview", "InfoRequested", "Approved", "Rejected" };

    public static readonly IReadOnlySet<string> AssigneeLiterals =
        new HashSet<string>(StringComparer.Ordinal) { "me", "unassigned" };
}

public sealed record ReviewAnnotationDto(Guid Id, DateTimeOffset RequestedAt, string Reason, IReadOnlyList<string> FlaggedProfileFields, IReadOnlyList<string> FlaggedDocumentTypeCodes, DateTimeOffset? ResolvedAt);

public sealed record ErpSyncDto(string? ExternalId, string SyncStatus, DateTimeOffset? LastSyncedAt);

public sealed record ReviewerSupplierViewDto(
    SupplierDto Supplier,
    ErpSyncDto ErpSync,
    IReadOnlyList<DocumentTypeStatusDto> Documents,
    IReadOnlyList<ReviewAnnotationDto> AnnotationHistory,
    uint RowVersion);
