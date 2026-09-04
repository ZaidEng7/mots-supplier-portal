using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Storage;

namespace MotsSupplierPortal.Infrastructure.Proposals;

/// <summary>
/// T-028's supplier half: a supplier reading a file on their own proposal.
///
/// <para>No envelope question arises here. The two-envelope rule keeps a buyer from seeing pricing
/// before the technical gate opens; it has nothing to say about a bidder reading their own bid, and
/// a rule applied where it does not belong is how a supplier ends up locked out of their own
/// upload.</para>
///
/// <para>The document is resolved THROUGH the proposal, which is itself resolved through
/// <see cref="ProposalLoader.LoadByProposalCodeAsync"/> - so the SupplierId predicate is in the
/// query and a document id belonging to someone else's proposal is a miss, not a leak.</para>
/// </summary>
public sealed class GetOwnProposalDocumentDownloadUrlHandler(
    AppDbContext db, IScopeContext scope, IFileStorage fileStorage, IAuditLogger auditLogger,
    AttachmentScanner attachmentScanner)
    : IGetOwnProposalDocumentDownloadUrlHandler
{
    public async Task<ProposalDocumentDownloadResult> HandleAsync(
        string proposalReferenceCode, Guid documentId, CancellationToken ct)
    {
        var loaded = await ProposalLoader.LoadByProposalCodeAsync(db, scope, proposalReferenceCode, ct);
        if (loaded is null) return new ProposalDocumentDownloadResult.NotFoundOrForbidden();
        var (_, proposal) = loaded.Value;

        var document = proposal.Documents.FirstOrDefault(d => d.Id == documentId);
        if (document is null) return new ProposalDocumentDownloadResult.NotFoundOrForbidden();

        return await ProposalDocumentDownload.MintAsync(
            db, scope, fileStorage, auditLogger, attachmentScanner, proposal, document, ct);
    }
}

/// <summary>
/// T-028's buyer half: reading the files attached to someone else's bid.
///
/// <para><b>What "before consolidation" means, precisely.</b> The gate is the EVALUATION's state,
/// not the proposal's. It opens when the evaluation for this RFQ reaches
/// <see cref="EvaluationState.Consolidated"/> or Finalized - the same predicate
/// <c>ComparisonHandlers</c> uses to decide whether any cross-proposal content is visible, reused
/// rather than restated. This matters now in a way it did not before batch 7: the proposal state
/// machine's middle became reachable, so proposals genuinely sit in UnderReview and Shortlisted
/// while officers work. A gate keyed on proposal state would open at Shortlisted, which is exactly
/// during scoring. Keyed on evaluation state, it opens once, after every evaluator has finished and
/// the results have been averaged - which is the moment the two-envelope seal is designed to
/// break.</para>
///
/// <para><b>D-7: both envelopes are gated, for now.</b> Technical files are not released early even
/// though the envelope field could support it. Releasing them early is a real product question -
/// evaluators arguably need the technical pack DURING scoring, which is the same problem
/// MyEvaluationDto has (it hands an evaluator a list of proposal GUIDs and no bid content at all).
/// That is a larger hole than T-028 and is recorded separately rather than solved by widening this
/// predicate on the way past.</para>
/// </summary>
public sealed class GetProposalDocumentsForBuyerHandler(AppDbContext db, IScopeContext scope)
    : IGetProposalDocumentsForBuyerHandler
{
    public async Task<IReadOnlyList<ProposalDocumentListItemDto>?> HandleAsync(
        string rfqReferenceCode, Guid proposalId, CancellationToken ct)
    {
        var proposal = await BuyerVisibleProposal.LoadAsync(db, scope, rfqReferenceCode, proposalId, ct);
        if (proposal is null) return null;

        return [.. proposal.Documents
            .OrderBy(d => d.UploadedAt)
            .Select(d => new ProposalDocumentListItemDto(
                d.Id, d.OriginalFileName, d.ContentType, d.Caption, d.UploadedAt, d.Envelope))];
    }
}

public sealed class GetProposalDocumentDownloadUrlForBuyerHandler(
    AppDbContext db, IScopeContext scope, IFileStorage fileStorage, IAuditLogger auditLogger,
    AttachmentScanner attachmentScanner)
    : IGetProposalDocumentDownloadUrlForBuyerHandler
{
    public async Task<ProposalDocumentDownloadResult> HandleAsync(
        string rfqReferenceCode, Guid proposalId, Guid documentId, CancellationToken ct)
    {
        var proposal = await BuyerVisibleProposal.LoadAsync(db, scope, rfqReferenceCode, proposalId, ct);
        if (proposal is null) return new ProposalDocumentDownloadResult.NotFoundOrForbidden();

        var document = proposal.Documents.FirstOrDefault(d => d.Id == documentId);
        if (document is null) return new ProposalDocumentDownloadResult.NotFoundOrForbidden();

        return await ProposalDocumentDownload.MintAsync(
            db, scope, fileStorage, auditLogger, attachmentScanner, proposal, document, ct);
    }
}

/// <summary>The buyer-side visibility predicate, written once so the list and the download cannot
/// drift apart - a list that shows a file the download refuses is a bug report, and a download that
/// serves a file the list hides is a leak.</summary>
internal static class BuyerVisibleProposal
{
    public static async Task<Proposal?> LoadAsync(
        AppDbContext db, IScopeContext scope, string rfqReferenceCode, Guid proposalId, CancellationToken ct)
    {
        // A supplier never reaches the buyer surface, even one who happens to hold the permission.
        if (scope.SupplierId is not null) return null;

        var rfq = await db.Rfqs.AsNoTracking()
            .Where(r => r.ReferenceCode == rfqReferenceCode && r.OrganizationId == scope.OrganizationId)
            .Select(r => new { r.Id })
            .FirstOrDefaultAsync(ct);
        if (rfq is null) return null;

        var evaluationState = await db.Evaluations.AsNoTracking()
            .Where(e => e.RfqId == rfq.Id)
            .Select(e => (EvaluationState?)e.State)
            .FirstOrDefaultAsync(ct);

        // The gate. Null covers "no evaluation has been opened", which is emphatically before
        // consolidation rather than a special case that skips it.
        if (evaluationState is not (EvaluationState.Consolidated or EvaluationState.Finalized)) return null;

        return await db.Proposals
            .Include(p => p.Documents)
            .FirstOrDefaultAsync(p => p.Id == proposalId && p.RfqId == rfq.Id, ct);
    }
}

/// <summary>The half both callers share: scan, audit, mint. Kept together because the ORDER is the
/// security property - the scan gate runs before a URL exists, not after.</summary>
internal static class ProposalDocumentDownload
{
    private static readonly TimeSpan UrlLifetime = TimeSpan.FromMinutes(5);

    public static async Task<ProposalDocumentDownloadResult> MintAsync(
        AppDbContext db, IScopeContext scope, IFileStorage fileStorage, IAuditLogger auditLogger,
        AttachmentScanner attachmentScanner, Proposal proposal, ProposalDocument document, CancellationToken ct)
    {
        var safe = await attachmentScanner.EnsureScannedAsync(
            document.ScanState, document.StorageKey, document.MarkScanClean, document.MarkScanRejected, ct);

        if (!safe)
        {
            await auditLogger.LogAsync("ProposalDocument", document.Id, "proposal_document_scan_rejected",
                scope.UserId, referenceCode: proposal.ReferenceCode, ct: ct);
            await db.SaveChangesAsync(ct);
            return new ProposalDocumentDownloadResult.NotFoundOrForbidden();
        }

        var url = await fileStorage.GetSignedDownloadUrlAsync(
            document.StorageKey, UrlLifetime, document.OriginalFileName, ct);

        // Who opened which bid document, and when, is the evidence a challenged award turns on.
        await auditLogger.LogAsync("ProposalDocument", document.Id, "proposal_document_access_granted",
            scope.UserId, referenceCode: proposal.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);

        return new ProposalDocumentDownloadResult.Success(url, document.OriginalFileName);
    }
}
