using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Storage;

namespace MotsSupplierPortal.Infrastructure.Proposals;

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
