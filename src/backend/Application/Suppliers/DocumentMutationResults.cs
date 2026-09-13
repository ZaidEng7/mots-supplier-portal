using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Application.Suppliers;

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

public abstract record DocumentDownloadUrlResult
{
    public sealed record Success(string Url) : DocumentDownloadUrlResult;
    public sealed record NotFoundOrForbidden : DocumentDownloadUrlResult;
}

public abstract record ReviewDocumentResult
{
    /// <param name="Document">The decided document, which is what the reviewer's screen re-renders.</param>
    /// <param name="SupplierRowVersion">
    /// The SUPPLIER aggregate's new version, not the document's - the document is a child and carries none.
    ///
    /// <para>P12 item 26's two remaining routes. Both approve and reject require an <c>If-Match</c>, because
    /// two reviewers deciding one document is the lost update worth refusing on this aggregate; and until now
    /// neither answered with a version, so a reviewer deciding a second document had no precondition to send
    /// and met a 428 that only a re-read of <c>GET /review/{referenceCode}</c> could clear. That read is the
    /// ETag's source, so this is the number it would have returned - the supplier root's.</para>
    ///
    /// <para>It travels in the RESULT rather than in the response body on purpose: the body is the document,
    /// §3 says so, and a version field on a document DTO that is really the supplier's version is a trap for
    /// the next reader. The endpoint puts it on the ETag header, where §8.1 already says a version lives.</para>
    /// </param>
    public sealed record Success(SupplierDocumentDto Document, uint SupplierRowVersion) : ReviewDocumentResult;
    public sealed record NotFoundOrForbidden : ReviewDocumentResult;
    public sealed record InvalidState(string Reason) : ReviewDocumentResult;
}
