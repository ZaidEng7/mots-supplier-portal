using MotsSupplierPortal.Domain.Proposals;

namespace MotsSupplierPortal.Application.Proposals;

/// <summary>
/// T-028: reading a proposal's supporting files.
///
/// <para>Upload and delete existed from FEAT-09.3; there was no read path at all, on either side.
/// A supplier could not re-open the compliance document they had attached to their own bid, and a
/// buyer could not open a bid document under any circumstances - the buyer case was not a
/// permission gap, it was the absence of any buyer-side read of a proposal whatsoever (see
/// ProposalDtoMapper's own note: no code path built proposal content for a proposal that was not
/// the caller's own).</para>
/// </summary>
public abstract record ProposalDocumentDownloadResult
{
    public sealed record Success(string Url, string FileName) : ProposalDocumentDownloadResult;

    /// <summary>
    /// One answer for "no such document", "not your proposal", "not your organization's RFQ",
    /// "the evaluation has not been consolidated" and "the scanner rejected this file".
    ///
    /// <para>§9.2 for the first three. The fourth is deliberate too: a buyer probing before
    /// consolidation must not be able to distinguish "this proposal has three attachments I cannot
    /// see yet" from "this proposal has none", because attachment COUNT is itself a signal about a
    /// competitor's bid during a live evaluation. The fifth follows T-025's rule that a refusal
    /// never confirms an upload arrived.</para>
    /// </summary>
    public sealed record NotFoundOrForbidden : ProposalDocumentDownloadResult;
}

/// <summary>One row of the buyer-side document list - the same shape the supplier sees, minus
/// nothing, because by the time a buyer can see this list the gate has already opened.</summary>
public sealed record ProposalDocumentListItemDto(
    Guid Id, string OriginalFileName, string ContentType, string? Caption,
    DateTimeOffset UploadedAt, ProposalDocumentEnvelope Envelope);

public interface IGetOwnProposalDocumentDownloadUrlHandler
{
    Task<ProposalDocumentDownloadResult> HandleAsync(string proposalReferenceCode, Guid documentId, CancellationToken ct);
}

public interface IGetProposalDocumentsForBuyerHandler
{
    /// <summary>Null - not an empty list - when the caller may not see this proposal's files at
    /// all, so the endpoint can answer 404 rather than an empty 200 that would confirm the
    /// proposal exists.</summary>
    Task<IReadOnlyList<ProposalDocumentListItemDto>?> HandleAsync(
        string rfqReferenceCode, Guid proposalId, CancellationToken ct);
}

/// <summary>
/// T-067: an assigned evaluator opening a TECHNICAL supporting file on a bid they are scoring.
///
/// <para>Separate from the buyer handler because the gate is different, not because the work is:
/// the buyer's opens at Consolidated and covers both envelopes, this one opens on assignment and
/// covers Technical only. Both mint through the same ProposalDocumentDownload.MintAsync, so the
/// scan gate and the audit row cannot diverge between them.</para>
/// </summary>
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
