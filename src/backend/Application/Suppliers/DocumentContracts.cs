namespace MotsSupplierPortal.Application.Suppliers;

public sealed record SupplierDocumentDto(
    Guid Id,
    int Version,
    string State,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    DateOnly? IssueDate,
    DateOnly? ExpiryDate,
    string? RejectReason,
    DateTimeOffset UploadedAt,
    DateTimeOffset? ReviewedAt);

public sealed record DocumentTypeStatusDto(
    Guid DocumentTypeId,
    string Code,
    string NameAr,
    string NameEn,
    bool IsRequired,
    bool ExpiryTracked,
    SupplierDocumentDto? LatestDocument);

public interface IListSupplierDocumentsHandler
{
    /// <summary>Row-scoped by the caller's own SupplierId; reviewers use the reviewer-scoped
    /// overload on <see cref="MotsSupplierPortal.Application.Suppliers.IReviewerListDocumentsHandler"/> instead.</summary>
    Task<IReadOnlyList<DocumentTypeStatusDto>> HandleOwnAsync(CancellationToken ct);
}

public interface IReviewerListDocumentsHandler
{
    Task<IReadOnlyList<DocumentTypeStatusDto>?> HandleAsync(Guid supplierId, CancellationToken ct);
}

public abstract record UploadDocumentResult
{
    public sealed record Success(SupplierDocumentDto Document) : UploadDocumentResult;
    public sealed record NotFoundOrOutOfScope : UploadDocumentResult;
    public sealed record InvalidDocumentType : UploadDocumentResult;
    public sealed record TooLarge : UploadDocumentResult;
    public sealed record UnsupportedType : UploadDocumentResult;
    /// <summary>BRULE-020: a type that tracks expiry needs a valid future date. Carries the domain's
    /// own message so the uploader is told what is wrong, not merely that something is.</summary>
    public sealed record InvalidExpiry(string Message) : UploadDocumentResult;
    public sealed record ContentMismatch : UploadDocumentResult;
    public sealed record NotEditable(string Reason) : UploadDocumentResult;
}

public sealed record UploadDocumentCommand(
    Guid DocumentTypeId,
    Stream Content,
    string OriginalFileName,
    string DeclaredContentType,
    long SizeBytes,
    DateOnly? IssueDate,
    DateOnly? ExpiryDate);

public interface IUploadDocumentHandler
{
    Task<UploadDocumentResult> HandleAsync(UploadDocumentCommand command, CancellationToken ct);
}

public abstract record DocumentDownloadUrlResult
{
    public sealed record Success(string Url) : DocumentDownloadUrlResult;
    public sealed record NotFoundOrForbidden : DocumentDownloadUrlResult;
}

public interface IGetDocumentDownloadUrlHandler
{
    Task<DocumentDownloadUrlResult> HandleAsync(Guid documentId, CancellationToken ct);
}

public abstract record ReviewDocumentResult
{
    public sealed record Success(SupplierDocumentDto Document) : ReviewDocumentResult;
    public sealed record NotFoundOrForbidden : ReviewDocumentResult;
    public sealed record InvalidState(string Reason) : ReviewDocumentResult;
}

public interface IApproveDocumentHandler
{
    Task<ReviewDocumentResult> HandleAsync(Guid documentId, CancellationToken ct);
}

public interface IRejectDocumentHandler
{
    Task<ReviewDocumentResult> HandleAsync(Guid documentId, string reason, CancellationToken ct);
}
