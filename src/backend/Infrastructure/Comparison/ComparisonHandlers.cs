// Builds the bid comparison: a view derived from the bids and the evaluation, storing nothing of its own.
//
//
// THE TWO GATES ARE BOTH ENFORCED HERE, IN ONE PLACE
//
// One decision decides which bids get their priced lines and their evaluation-derived fields at all.
//
// Before the evaluation's scores have been gathered, that set is empty for every bid. Not empty for
// unscored bids: literally no bid has passed qualification yet as far as this view is concerned. Peer
// scores are unreadable until then, and a comparison is by definition a view across evaluators, so it
// cannot show anything evaluation-derived one instant before every other blind-scoring read does.
//
// Per-criterion scores are averaged from the individual scores fresh on every read rather than stored, so
// there is one place that decides what a consolidated score is.

namespace MotsSupplierPortal.Infrastructure.Comparison;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Comparison;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Infrastructure.Persistence;
using EvaluationAggregate = MotsSupplierPortal.Domain.Evaluation.Evaluation;

public sealed class GetComparisonHandler(AppDbContext db, IScopeContext scope) : IGetComparisonHandler
{
    public async Task<ComparisonDto?> HandleAsync(string rfqReferenceCode, CancellationToken ct)
    {
        if (scope.OrganizationId is null) return null;

        var rfq = await db.Rfqs.Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.ReferenceCode == rfqReferenceCode && r.OrganizationId == scope.OrganizationId, ct);
        if (rfq is null) return null;

        var proposals = await db.Proposals
            .Include(p => p.RequirementAnswers)
            .Where(p => p.RfqId == rfq.Id && ProposalStates.UnderComparison.Contains(p.State))
            .ToListAsync(ct);

        var supplierNames = await db.Suppliers
            .Where(s => proposals.Select(p => p.SupplierId).Contains(s.Id))
            .Select(s => new { s.Id, s.DisplayNameAr, s.DisplayNameEn })
            .ToDictionaryAsync(s => s.Id, ct);

        var requirements = await db.Requirements.Where(q => q.RfqId == rfq.Id).ToListAsync(ct);

        var evaluation = await db.Evaluations
            .Include(e => e.Criteria).Include(e => e.Results).Include(e => e.Scores)
            .AsSplitQuery()
            .FirstOrDefaultAsync(e => e.RfqId == rfq.Id, ct);

        var consolidatedOrLater = evaluation is not null && evaluation.State is EvaluationState.Consolidated or EvaluationState.Finalized;
        var qualifiedProposalIds = consolidatedOrLater
            ? evaluation!.Results.Where(r => r.TechnicallyQualified).Select(r => r.ProposalId).ToHashSet()
            : [];

        var itemsByProposal = qualifiedProposalIds.Count == 0
            ? new Dictionary<Guid, List<ProposalItem>>()
            : await db.ProposalItems.Where(i => qualifiedProposalIds.Contains(i.ProposalId))
                .GroupBy(i => i.ProposalId).ToDictionaryAsync(g => g.Key, g => g.ToList(), ct);

        var proposalDtos = proposals.Select(p =>
        {
            var supplier = supplierNames[p.SupplierId];
            var requirementDtos = requirements.Select(q => new ComparisonRequirementAnswerDto(
                q.Id, q.TextAr, q.TextEn, q.IsMandatory,
                Answered: p.RequirementAnswers.Any(a => a.RequirementId == q.Id))).ToList();

            List<ComparisonItemPriceDto>? itemDtos = null;
            decimal? grandTotal = null;
            if (itemsByProposal.TryGetValue(p.Id, out var items))
            {
                itemDtos = items.Select(i => new ComparisonItemPriceDto(i.RfqItemId, i.Quantity, i.UnitPrice, i.Discount, i.LineTotal)).ToList();
                grandTotal = itemDtos.Sum(i => i.LineTotal);
            }

            bool? technicallyQualified = null;
            decimal? technicalWeighted = null, financialWeighted = null, weightedTotal = null;
            int? rank = null;
            var tieUnresolved = false;
            string? tieResolutionReason = null;
            List<ComparisonCriterionScoreDto>? criterionScores = null;

            if (consolidatedOrLater)
            {
                var result = evaluation!.Results.FirstOrDefault(r => r.ProposalId == p.Id);
                technicallyQualified = result?.TechnicallyQualified ?? false;
                technicalWeighted = result?.TechnicalWeightedScore;
                financialWeighted = result?.FinancialWeightedScore;
                weightedTotal = result?.WeightedTotal;
                rank = result?.Rank;
                tieUnresolved = result?.TieUnresolved ?? false;
                tieResolutionReason = result?.TieResolutionReason;

                criterionScores = evaluation.Criteria.Select(c =>
                {
                    var scoresForCriterion = evaluation.Scores.Where(s => s.ProposalId == p.Id && s.CriterionId == c.Id).ToList();
                    var average = scoresForCriterion.Count == 0 ? 0m : scoresForCriterion.Average(s => s.RawScore);
                    bool? metThreshold = c.Threshold is null ? null : average >= c.Threshold;
                    return new ComparisonCriterionScoreDto(c.Id, c.NameAr, c.NameEn, c.IsFinancial, c.Weight, c.MaxScore, c.Threshold, average, metThreshold);
                }).ToList();
            }

            return new ComparisonProposalDto(
                p.ReferenceCode, p.SupplierId, supplier.DisplayNameAr, supplier.DisplayNameEn,
                p.CurrencyCode, p.PaymentTerms, p.IncotermCode, p.DeliveryTermsAr, p.DeliveryTermsEn,
                p.Warranty, p.ValidityEnd, p.SubmittedAt!.Value,
                requirementDtos, itemDtos, grandTotal,
                technicallyQualified, technicalWeighted, financialWeighted, weightedTotal, rank, tieUnresolved, tieResolutionReason, criterionScores);
        }).ToList();

        return new ComparisonDto(
            rfq.ReferenceCode, rfq.TitleAr, rfq.TitleEn, evaluation?.State.ToString() ?? nameof(EvaluationState.NotStarted),
            [.. rfq.Items.OrderBy(i => i.LineNo).Select(i => new ComparisonRfqItemDto(i.Id, i.LineNo, i.TitleAr, i.TitleEn, i.Quantity, i.UnitOfMeasureCode))],
            proposalDtos);
    }
}
