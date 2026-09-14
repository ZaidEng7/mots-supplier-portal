// What the document reads and writes are called.
//
//
// TWO LISTS, BECAUSE THEY ANSWER DIFFERENT QUESTIONS
//
// The supplier's own list is their registration checklist, one row per document type, scoped to their own
// company.
//
// The back-office list is one row per document, paged, with each document's own state and upload time.
//
// That is deliberately not the checklist shape. "Which required types does this supplier still owe" and "what
// documents are on file" are different questions, and one shape answering both would answer neither well.

namespace MotsSupplierPortal.Application.Suppliers;

using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Suppliers;

public interface IListSupplierDocumentsHandler
{
    Task<IReadOnlyList<DocumentTypeStatusDto>> HandleOwnAsync(CancellationToken ct);
}

public interface IGetSupplierDocumentHandler
{
    Task<SupplierDocumentDto?> HandleAsync(string supplierCode, string documentCode, CancellationToken ct);
}

public interface IListSupplierDocumentsPagedHandler
{
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

public interface IGetDocumentHistoryHandler
{
    Task<IReadOnlyList<SupplierDocumentDto>?> HandleAsync(string supplierCode, string documentTypeCode, CancellationToken ct);
}
