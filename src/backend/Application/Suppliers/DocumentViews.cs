using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Application.Suppliers;

public sealed record SupplierDocumentDto(
    /// <summary>T-010: the public code. §3 keeps internal GUIDs out of payloads as well as URLs, so
    /// the aggregate's Guid is not emitted at all - a client that needs to address this document
    /// uses this value, which is the only identifier the API accepts.
    ///
    /// <para>Spelled <c>documentId</c> under R-9, matching §12.3 and matching
    /// SupplierDocumentListItemDto, which already used that name. The two document DTOs had been
    /// naming the same value two different ways.</para></summary>
    string DocumentId,
    int Version,
    string State,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    DateOnly? IssueDate,
    DateOnly? ExpiryDate,
    string? RejectReason,
    DateTimeOffset UploadedAt,
    DateTimeOffset? ReviewedAt,
    /// <summary>
    /// T-015: §12.3 shows <c>scanStatus</c> beside <c>state</c> - <c>{ "state": "Uploaded",
    /// "scanStatus": "Pending" }</c>. This schema folds the scan into the state machine, exactly as
    /// it folds expiry in (see SupplierDocumentListItemDto's note on <c>expiryState</c>), so the
    /// field is DERIVED from the state rather than stored. A second stored copy of a fact the state
    /// already carries is a second thing to keep in step.
    /// </summary>
    string ScanStatus = "Clean");

public sealed record DocumentTypeStatusDto(
    Guid DocumentTypeId,
    string Code,
    string NameAr,
    string NameEn,
    bool IsRequired,
    bool ExpiryTracked,
    SupplierDocumentDto? LatestDocument);

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
