namespace MotsSupplierPortal.Application.Suppliers;

public sealed record ReviewQueueItemDto(string ReferenceCode, string DisplayNameAr, string DisplayNameEn, string OnboardingState);

public interface IListReviewQueueHandler
{
    /// <summary>Submitted/UnderReview/Resubmitted applications - the reviewer's work queue.</summary>
    Task<IReadOnlyList<ReviewQueueItemDto>> HandleAsync(CancellationToken ct);
}

public sealed record ReviewAnnotationDto(Guid Id, DateTimeOffset RequestedAt, string Reason, IReadOnlyList<string> FlaggedProfileFields, IReadOnlyList<string> FlaggedDocumentTypeCodes, DateTimeOffset? ResolvedAt);

/// <summary>FEAT-04.10: ERP mapping fields - read-only to staff, never exposed to the supplier
/// (see SupplierDto's own doc comment for the other half of that split).</summary>
public sealed record ErpSyncDto(string? ExternalId, string SyncStatus, DateTimeOffset? LastSyncedAt);

public sealed record ReviewerSupplierViewDto(SupplierDto Supplier, ErpSyncDto ErpSync, IReadOnlyList<DocumentTypeStatusDto> Documents, IReadOnlyList<ReviewAnnotationDto> AnnotationHistory);

public interface IGetReviewerSupplierViewHandler
{
    Task<ReviewerSupplierViewDto?> HandleAsync(string referenceCode, CancellationToken ct);
}

public interface IGetOwnActiveAnnotationHandler
{
    /// <summary>The supplier's own view of why they're InfoRequested and what's flagged - the
    /// reviewer-side annotation history is staff-only, but the supplier needs to know what to fix.</summary>
    Task<ReviewAnnotationDto?> HandleAsync(CancellationToken ct);
}

public abstract record ReviewDecisionResult
{
    public sealed record Success(SupplierDto Supplier) : ReviewDecisionResult;
    public sealed record NotFound : ReviewDecisionResult;
    public sealed record InvalidState(string Reason) : ReviewDecisionResult;
}

public interface IPickUpApplicationHandler
{
    Task<ReviewDecisionResult> HandleAsync(string referenceCode, CancellationToken ct);
}

public interface IApproveApplicationHandler
{
    Task<ReviewDecisionResult> HandleAsync(string referenceCode, CancellationToken ct);
}

public interface IRejectApplicationHandler
{
    Task<ReviewDecisionResult> HandleAsync(string referenceCode, string reason, CancellationToken ct);
}

public sealed record RequestInfoCommand(string ReferenceCode, string Reason, IReadOnlyList<string> FlaggedProfileFields, IReadOnlyList<string> FlaggedDocumentTypeCodes);

public interface IRequestInfoHandler
{
    Task<ReviewDecisionResult> HandleAsync(RequestInfoCommand command, CancellationToken ct);
}

public interface IResubmitApplicationHandler
{
    Task<ReviewDecisionResult> HandleAsync(CancellationToken ct);
}
