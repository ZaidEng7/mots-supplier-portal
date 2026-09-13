// Reading a bid's supporting files, on both sides.
//
// Uploading and deleting existed and there was no read path at all. A supplier could not re-open the
// compliance document they had attached to their own bid, and a buyer could not open a bid document under any
// circumstances.
//
// The buyer's half was not a permission gap. There was no buyer-side read of a bid's content whatsoever: no
// code path built it for a bid that was not the caller's own.

namespace MotsSupplierPortal.Application.Proposals;

using MotsSupplierPortal.Domain.Proposals;

public abstract record ProposalDocumentDownloadResult
{
    public sealed record Success(string Url, string FileName) : ProposalDocumentDownloadResult;

    public sealed record NotFoundOrForbidden : ProposalDocumentDownloadResult;
}

public sealed record ProposalDocumentListItemDto(
    Guid Id, string OriginalFileName, string ContentType, string? Caption,
    DateTimeOffset UploadedAt, ProposalDocumentEnvelope Envelope);

public interface IGetOwnProposalDocumentDownloadUrlHandler
{
    Task<ProposalDocumentDownloadResult> HandleAsync(string proposalReferenceCode, Guid documentId, CancellationToken ct);
}

public interface IGetProposalDocumentsForBuyerHandler
{
    Task<IReadOnlyList<ProposalDocumentListItemDto>?> HandleAsync(
        string rfqReferenceCode, Guid proposalId, CancellationToken ct);
}

public interface IGetProposalDocumentDownloadUrlForEvaluatorHandler
{
    Task<ProposalDocumentDownloadResult> HandleAsync(
        string rfqReferenceCode, string proposalCode, Guid documentId, CancellationToken ct);
}

public interface IGetProposalDocumentDownloadUrlForBuyerHandler
{
    Task<ProposalDocumentDownloadResult> HandleAsync(
        string rfqReferenceCode, Guid proposalId, Guid documentId, CancellationToken ct);
}
