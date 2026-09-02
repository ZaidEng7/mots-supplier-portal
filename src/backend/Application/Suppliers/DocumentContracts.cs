using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Suppliers;

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
    /// <summary>Row-scoped by the caller's own SupplierId - the supplier's own onboarding
    /// checklist, one row per document TYPE.</summary>
    Task<IReadOnlyList<DocumentTypeStatusDto>> HandleOwnAsync(CancellationToken ct);
}

/// <summary>
/// §12.3's back-office document list: one row per DOCUMENT, page-mode paginated.
///
/// <para>Deliberately NOT the checklist shape above. §12.3's worked response is a list of documents
/// with their own states and upload times, which is a different question from "which required types
/// does this supplier still owe".</para>
/// </summary>
public interface IListSupplierDocumentsPagedHandler
{
    /// <summary>Null when no supplier carries <paramref name="supplierCode"/>.</summary>
    Task<ListEnvelope<SupplierDocumentListItemDto>?> HandleAsync(
        string supplierCode, string? state, int page, int? pageSize, CancellationToken ct);
}

/// <summary>
/// §12.3's documented row. Field-by-field against that response, with two divergences named rather
/// than papered over:
///
/// <list type="bullet">
///   <item><b>documentId</b> - §12.3 shows <c>"DOC-2026-013377"</c>, a public short code.
///   SupplierDocument has no reference code; its identity is a Guid. §3.1 forbids exposing GUIDs in
///   PATHS, and this is a body field, so the Guid is emitted - but the documented SHAPE cannot be
///   produced without minting document codes, which is out of scope for this batch.</item>
///   <item><b>expiryState</b> - §12.3 models expiry as a field orthogonal to <c>state</c>
///   (<c>"state": "UnderReview"</c> alongside <c>"expiryState": "Valid"</c>). This schema folds
///   expiry INTO the state machine: ExpiringSoon and Expired are DocumentState members. The field
///   is therefore derived from the state rather than stored, and is null for a type that does not
///   track expiry at all.</item>
/// </list>
/// </summary>
public sealed record SupplierDocumentListItemDto(
    Guid DocumentId,
    string DocumentTypeCode,
    DocumentState State,
    DateOnly? ExpiresAt,
    string? ExpiryState,
    string? DownloadUrl,
    DateTimeOffset UploadedAt);

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
