// Listing the files attached to somebody else's bid, as buyer staff.
//
//
// WHAT "BEFORE CONSOLIDATION" MEANS, PRECISELY
//
// The gate is the EVALUATION's state, not the bid's. It opens once the evaluation for this tender has been
// consolidated or finalised, which is the same test the comparison screen uses to decide whether any
// cross-bid content is visible, reused rather than restated.
//
// That distinction matters now in a way it did not before the middle of the bid lifecycle became reachable.
// Bids genuinely sit in review and shortlisted while officers work, so a gate keyed on the bid's state would
// open at shortlisted, which is exactly during scoring.
//
// Keyed on the evaluation's state, it opens once, after every evaluator has finished and the results have
// been averaged, which is the moment the two-envelope seal is designed to break.
//
//
// BOTH ENVELOPES ARE GATED, FOR NOW
//
// Technical files are not released early even though the envelope field could support it.
//
// Releasing them early is a real product question: evaluators arguably need the technical pack DURING
// scoring, which is the same gap the evaluator's own workspace had. That is a larger hole and is recorded
// separately rather than solved by widening this test on the way past.

namespace MotsSupplierPortal.Infrastructure.Proposals;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Storage;

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
