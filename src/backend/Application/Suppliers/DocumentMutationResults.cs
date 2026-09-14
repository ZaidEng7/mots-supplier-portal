// What the document writes can answer: uploading, asking for a download link, and a reviewer's decision.
//
// An expiry-date refusal carries the domain's own message, so the uploader is told what is wrong with the date
// rather than merely that something is.
//
//
// A DECISION RETURNS THE SUPPLIER'S VERSION, NOT THE DOCUMENT'S
//
// A document is part of a supplier and carries no version of its own.
//
// Both approving and rejecting require the caller to say which version they read, because two reviewers
// deciding one document is the lost update worth refusing here. The version that guards it is the supplier's,
// and it is what the reviewer's own read issues, so a reviewer deciding a second document already holds what
// the next write needs.

namespace MotsSupplierPortal.Application.Suppliers;

using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Suppliers;

public abstract record UploadDocumentResult
{
    public sealed record Success(SupplierDocumentDto Document) : UploadDocumentResult;
    public sealed record NotFoundOrOutOfScope : UploadDocumentResult;
    public sealed record InvalidDocumentType : UploadDocumentResult;
    public sealed record TooLarge : UploadDocumentResult;
    public sealed record UnsupportedType : UploadDocumentResult;
    public sealed record InvalidExpiry(string Message) : UploadDocumentResult;
    public sealed record ContentMismatch : UploadDocumentResult;
    public sealed record NotEditable(string Reason) : UploadDocumentResult;
}

public abstract record DocumentDownloadUrlResult
{
    public sealed record Success(string Url) : DocumentDownloadUrlResult;
    public sealed record NotFoundOrForbidden : DocumentDownloadUrlResult;
}

public abstract record ReviewDocumentResult
{
    public sealed record Success(SupplierDocumentDto Document, uint SupplierRowVersion) : ReviewDocumentResult;
    public sealed record NotFoundOrForbidden : ReviewDocumentResult;
    public sealed record InvalidState(string Reason) : ReviewDocumentResult;
}
