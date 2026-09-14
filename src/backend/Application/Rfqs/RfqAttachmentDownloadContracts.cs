// Retrieving a tender's own documents.
//
// Uploading and deleting existed and reading did not, so a buyer could attach the specification an invited
// supplier is meant to bid against and that supplier could never open it.
//
// One outcome covers no such attachment, a tender belonging to another organization, and a supplier who was
// not invited. An out-of-scope read of something that exists answers exactly as a read of something that does
// not, so the API never reveals which it was.

namespace MotsSupplierPortal.Application.Rfqs;

public abstract record RfqAttachmentDownloadResult
{
    public sealed record Success(string Url, string FileName) : RfqAttachmentDownloadResult;

    public sealed record NotFoundOrForbidden : RfqAttachmentDownloadResult;
}

public interface IGetRfqAttachmentDownloadUrlHandler
{
    Task<RfqAttachmentDownloadResult> HandleAsync(string rfqReferenceCode, Guid attachmentId, CancellationToken ct);
}
