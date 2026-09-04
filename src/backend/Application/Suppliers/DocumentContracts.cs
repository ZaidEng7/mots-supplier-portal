using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Application.Suppliers;

public sealed record SupplierDocumentDto(
    /// <summary>T-010: the public code. §3 keeps internal GUIDs out of payloads as well as URLs, so
    /// the aggregate's Guid is not emitted at all - a client that needs to address this document
    /// uses this value, which is the only identifier the API accepts.</summary>
    string Id,
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
///   <item><b>documentId</b> - RESOLVED (T-010). This now emits <c>DOC-2026-000001</c>, the shape
///   §12.3 documents. The previous note here claimed §3.1 governs only PATHS and that a Guid in a
///   body was therefore acceptable; that reading was wrong. §3 principle 3 says internal GUIDs are
///   "never exposed in URLs, PAYLOADS, or errors", and §12's own checklist repeats it as "Public ids
///   only in paths/bodies (no GUID/int leakage)".</item>
///   <item><b>expiryState</b> - §12.3 models expiry as a field orthogonal to <c>state</c>
///   (<c>"state": "UnderReview"</c> alongside <c>"expiryState": "Valid"</c>). This schema folds
///   expiry INTO the state machine: ExpiringSoon and Expired are DocumentState members. The field
///   is therefore derived from the state rather than stored, and is null for a type that does not
///   track expiry at all.</item>
/// </list>
/// </summary>
public sealed record SupplierDocumentListItemDto(
    string DocumentId,
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
    Task<DocumentDownloadUrlResult> HandleAsync(string documentCode, CancellationToken ct);
}

public abstract record ReviewDocumentResult
{
    public sealed record Success(SupplierDocumentDto Document) : ReviewDocumentResult;
    public sealed record NotFoundOrForbidden : ReviewDocumentResult;
    public sealed record InvalidState(string Reason) : ReviewDocumentResult;
}

public interface IApproveDocumentHandler
{
    Task<ReviewDocumentResult> HandleAsync(string documentCode, CancellationToken ct);
}

public interface IRejectDocumentHandler
{
    Task<ReviewDocumentResult> HandleAsync(string documentCode, string reason, CancellationToken ct);
}
