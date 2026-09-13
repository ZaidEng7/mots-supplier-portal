using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Application.Suppliers;

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
/// <summary>
/// T-012: the single-document read the upload's <c>Location</c> header points at.
///
/// <para>§12.3 documents <c>Location: /api/v1/suppliers/{supplierCode}/documents/{documentCode}</c>
/// and defines no GET for it, so the header named a resource that did not exist. Batch 8 left the
/// header non-conforming rather than emit a path resolving to nothing; this closes it the other way,
/// by making the documented path real - a 202 whose Location 404s is a worse contract than either.</para>
///
/// <para>Serves the supplier who owns it and a reviewer holding <c>supplier.document.review</c>, the
/// same two callers and the same row-scope rule as the download - null for a miss, an out-of-scope
/// document and an unknown code alike (§9.2).</para>
/// </summary>
public interface IGetSupplierDocumentHandler
{
    Task<SupplierDocumentDto?> HandleAsync(string supplierCode, string documentCode, CancellationToken ct);
}

public interface IListSupplierDocumentsPagedHandler
{
    /// <summary>Null when no supplier carries <paramref name="supplierCode"/>.</summary>
    Task<ListEnvelope<SupplierDocumentListItemDto>?> HandleAsync(
        string supplierCode, string? state, int page, int? pageSize, CancellationToken ct);
}

public interface IUploadDocumentHandler
{
    Task<UploadDocumentResult> HandleAsync(UploadDocumentCommand command, CancellationToken ct);
}

public interface IGetDocumentDownloadUrlHandler
{
    Task<DocumentDownloadUrlResult> HandleAsync(string documentCode, CancellationToken ct);
}

public interface IApproveDocumentHandler
{
    Task<ReviewDocumentResult> HandleAsync(string documentCode, CancellationToken ct);
}

public interface IRejectDocumentHandler
{
    Task<ReviewDocumentResult> HandleAsync(string documentCode, string reason, CancellationToken ct);
}

/// <summary>
/// SCR-132: every version of one document type for one supplier, newest first.
///
/// <para><b>The gap this closes.</b> <c>SupplierDocument</c> has carried <c>Version</c> and
/// <c>IsLatestVersion</c> since EPIC-05, so the chain has always existed in storage — and no endpoint
/// returned it. A supplier could see the current state of a document and never why it got there: a
/// rejection followed by a re-upload looked identical to a first upload that was approved.</para>
///
/// <para>Keyed by document TYPE code rather than by a document id, because the history is the type's
/// story: "what happened to my commercial registration", not "what happened to this one file".</para>
/// </summary>
public interface IGetDocumentHistoryHandler
{
    /// <summary>Null when the supplier is out of scope or the type is unknown — §9.2's 404 either
    /// way, since which of the two it was is not a distinction worth disclosing.</summary>
    Task<IReadOnlyList<SupplierDocumentDto>?> HandleAsync(string supplierCode, string documentTypeCode, CancellationToken ct);
}
