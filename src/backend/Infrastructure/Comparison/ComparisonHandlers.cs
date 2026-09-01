using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Comparison;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Infrastructure.Persistence;
using EvaluationAggregate = MotsSupplierPortal.Domain.Evaluation.Evaluation;

namespace MotsSupplierPortal.Infrastructure.Comparison;

/// <summary>FEAT-12.1..12.4/FR-CMP-001..004: a derived read-side view over Proposal + Evaluation - no
/// new aggregate, no new writes, exactly as EPIC-12's own domain note in BACKLOG.md says.
///
/// <para><b>The two-envelope gate and evaluation blindness, both enforced here (FEAT-12.4/FR-CMP-004,
/// OQ-009, BRULE-058):</b> <paramref name="qualifiedProposalIds"/>-equivalent logic below is the
/// single point that decides which proposal ids get ProposalItem rows and evaluation-derived fields
/// at all. Before the evaluation reaches Consolidated, that set is empty for EVERY proposal - not
/// "empty for unscored proposals", literally no proposal has passed qualification yet from the
/// comparison view's perspective, matching BRULE-058's "peer scores/comments not readable until
/// Consolidated" applied at the aggregate level (a comparison view is definitionally cross-evaluator,
/// so it cannot show anything evaluation-derived one instant before every other blind-scoring read
/// path in this codebase does). Per-criterion scores are averaged from EvaluatorScore fresh on every
/// read, gated by the exact same State check - there is no second, separately-cached copy of
/// "consolidated" data that could drift out of sync with the real gate.</para>
///
/// <para><b>No client-supplied sort/filter parameter exists on this endpoint at all</b> - the one
/// deliberate simplification that closes off the exact injection risk the task brief calls out
/// ("the query itself can't be coaxed into leaking a disqualified proposal's pricing or a
/// pre-consolidation evaluator score through a parameter or sort/filter option"). Highlighting/
/// sorting in the UI operates only on the DTO this handler already decided is safe to return -
/// there is no server-side query parameter whose value could change what gets loaded from the
/// EvaluatorScore/ProposalItem tables.</para></summary>
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
            .Where(p => p.RfqId == rfq.Id && p.State == ProposalState.Submitted)
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

        // The gate: empty until Consolidated+, regardless of anything any single evaluator has
        // scored - see this class's own doc comment.
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
            List<ComparisonCriterionScoreDto>? criterionScores = null;

            if (consolidatedOrLater)
            {
                var result = evaluation!.Results.FirstOrDefault(r => r.ProposalId == p.Id);
                technicallyQualified = result?.TechnicallyQualified ?? false;
                technicalWeighted = result?.TechnicalWeightedScore;
                financialWeighted = result?.FinancialWeightedScore;
                weightedTotal = result?.WeightedTotal;
                rank = result?.Rank;

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
                technicallyQualified, technicalWeighted, financialWeighted, weightedTotal, rank, criterionScores);
        }).ToList();

        return new ComparisonDto(
            rfq.ReferenceCode, rfq.TitleAr, rfq.TitleEn, evaluation?.State.ToString() ?? nameof(EvaluationState.NotStarted),
            [.. rfq.Items.OrderBy(i => i.LineNo).Select(i => new ComparisonRfqItemDto(i.Id, i.LineNo, i.TitleAr, i.TitleEn, i.Quantity, i.UnitOfMeasureCode))],
            proposalDtos);
    }
}
