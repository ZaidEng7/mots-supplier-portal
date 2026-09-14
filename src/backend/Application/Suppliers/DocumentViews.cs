// The shapes a compliance document is read through.
//
//
// THE PUBLIC CODE IS THE ONLY IDENTIFIER
//
// Internal identifiers stay out of payloads as well as out of addresses, so the record's own database key is
// not emitted at all. A client that needs to address a document uses the public code, which is the only
// identifier the API accepts.
//
// Both document shapes spell that field the same way now. They had been naming the same value two different
// ways.
//
//
// THE SCAN IS PART OF THE STATE
//
// The written contract shows the scan status beside the state as two separate fields. This folds the scan into
// the state machine instead, exactly as the domain does: a document waiting on a scan is in a state that says
// so.
//
// One value rather than two means there is no combination of the two that has to be ruled out, and no way for
// them to disagree.

namespace MotsSupplierPortal.Application.Suppliers;

using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Suppliers;

public sealed record SupplierDocumentDto(
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
    string ScanStatus = "Clean");

public sealed record DocumentTypeStatusDto(
    Guid DocumentTypeId,
    string Code,
    string NameAr,
    string NameEn,
    bool IsRequired,
    bool ExpiryTracked,
    SupplierDocumentDto? LatestDocument);

public sealed record SupplierDocumentListItemDto(
    string DocumentId,
    string DocumentTypeCode,
    DocumentState State,
    DateOnly? ExpiresAt,
    string? ExpiryState,
    string? DownloadUrl,
    DateTimeOffset UploadedAt);
