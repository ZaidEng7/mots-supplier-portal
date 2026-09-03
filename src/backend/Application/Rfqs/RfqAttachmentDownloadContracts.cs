namespace MotsSupplierPortal.Application.Rfqs;

/// <summary>
/// T3-01: retrieving an RFQ's tender documents.
///
/// <para>Upload existed from FEAT-07.2 and delete from the same story; there was no read path at
/// all, so a buyer could attach the specification an invited supplier is meant to bid against and
/// that supplier could never open it.</para>
/// </summary>
public abstract record RfqAttachmentDownloadResult
{
    public sealed record Success(string Url, string FileName) : RfqAttachmentDownloadResult;

    /// <summary>
    /// One result for "no such attachment", "not your organization's RFQ" and "you were not invited".
    ///
    /// <para>§9.2: <i>"Out-of-scope access to an existing resource returns 404 (not 403) to avoid
    /// leaking existence"</i>. A download is the widest direct-object read in the product and the id
    /// is the only thing a prober controls, so the three cases must be one answer.</para>
    /// </summary>
    public sealed record NotFoundOrForbidden : RfqAttachmentDownloadResult;
}

public interface IGetRfqAttachmentDownloadUrlHandler
{
    Task<RfqAttachmentDownloadResult> HandleAsync(string rfqReferenceCode, Guid attachmentId, CancellationToken ct);
}
