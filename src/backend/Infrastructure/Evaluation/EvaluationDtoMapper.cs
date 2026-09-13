using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using System.Globalization;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Evaluation;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;
using EvaluationAggregate = MotsSupplierPortal.Domain.Evaluation.Evaluation;

namespace MotsSupplierPortal.Infrastructure.Evaluation;

internal static class EvaluationDtoMapper
{
    /// <param name="names">Evaluator user id to display name. Passed in rather than looked up here because
    /// this mapper is static and has no DbContext - and the alternative, a GUID on the screen, is what a
    /// manager was actually reading before batch 11.</param>
    /// <param name="proposalCodes">Proposal id to §3 reference code. Same reason as <paramref name="names"/>:
    /// the alternative is a GUID on the screen where a tender is decided.</param>
    /// <summary>
    /// T-068: <paramref name="proposalCodes"/> is REQUIRED now.
    ///
    /// <para>It used to be optional, and six of the eight call sites here left it out - so those
    /// responses carried a null code beside a GUID, and the results table fell back to the GUID. An
    /// optional argument whose absence produces a database identifier on a manager's screen is not
    /// optional; it is a default that is wrong.</para>
    /// </summary>
    public static EvaluationDto ToDto(
        EvaluationAggregate evaluation, Rfq rfq,
        IReadOnlyDictionary<Guid, string> proposalCodes,
        IReadOnlyDictionary<Guid, string>? names = null) => new(
        evaluation.Id, evaluation.RfqId, rfq.ReferenceCode, evaluation.State,
        [.. evaluation.Criteria.Select(ToCriterionDto)],
        [.. evaluation.Assignments.Select(a => new EvaluationAssignmentDto(
            a.EvaluatorUserId, names?.GetValueOrDefault(a.EvaluatorUserId), a.AssignedAt, a.SubmittedAt, a.RecusedAt, a.RecusalReason))],
        [.. evaluation.Results
            // A result whose proposal row has gone is dropped rather than rendered with an invented
            // code. It cannot happen - a result is written from a live bid and nothing deletes
            // proposals - and if it ever does, a missing row is a smaller lie than a made-up one.
            .Where(r => proposalCodes.ContainsKey(r.ProposalId))
            .Select(r => new ConsolidatedResultDto(
                r.ProposalId, proposalCodes[r.ProposalId], r.TechnicallyQualified,
                r.TechnicalWeightedScore, r.FinancialWeightedScore, r.WeightedTotal, r.Rank, r.TieUnresolved, r.TieResolutionReason))],
        evaluation.RowVersion);

    public static EvaluationCriterionDto ToCriterionDto(EvaluationCriterionSnapshot c) =>
        new(c.Id, c.NameAr, c.NameEn, c.Dimension, c.Weight, c.MaxScore, c.Threshold, c.ScoringType, c.IsFinancial,
            c.RequiresJustification, c.GuidanceAr, c.GuidanceEn);

    /// <summary>
    /// T-067: the evaluator's workspace, with the bids and the specification on it.
    ///
    /// <para><paramref name="bids"/> arrives already loaded and already filtered to the technical
    /// envelope - this method does no querying, so there is no path by which a pricing row could be
    /// pulled in here by a later edit.</para>
    /// </summary>
    public static MyEvaluationDto ToMyDto(
        EvaluationAggregate evaluation, Rfq rfq, Guid evaluatorUserId, IReadOnlyList<EvaluatorBid> bids)
    {
        var assignment = evaluation.Assignments.First(a => a.EvaluatorUserId == evaluatorUserId && a.IsActive);
        var codeById = bids.ToDictionary(b => b.ProposalId, b => b.ProposalCode);

        var myScores = evaluation.Scores.Where(s => s.EvaluatorUserId == evaluatorUserId)
            // A score whose proposal is no longer in evaluation (withdrawn mid-scoring) has no code
            // to name it by, and showing a bid that left is worse than omitting it.
            .Where(s => codeById.ContainsKey(s.ProposalId))
            .Select(s => new MyScoreDto(codeById[s.ProposalId], s.CriterionId, s.RawScore, s.CommentAr, s.CommentEn, s.ScoredAt))
            .ToList();

        // A-8: bidders are anonymous WHILE this evaluator is scoring, and named at the two moments
        // where the name is the point.
        //
        // Before scoring opens, the evaluator is looking at the assignment they have been offered and
        // declaring conflicts - BRULE-067's recusal, which is an assignment-time act. After
        // consolidation the scores are in and locked, so a name can no longer influence one.
        //
        // Between those two, the name is withheld and a stable pseudonym stands in its place. Keyed on
        // THIS evaluator's own submission rather than on the evaluation's state, because the two
        // evaluators on a committee are not necessarily at the same point.
        // Revealed in exactly two windows, and the first is keyed on THIS evaluator's declaration rather
        // than on the evaluation's state: reading my-evaluation is itself what opens scoring (see
        // GetMyEvaluationHandler), and the evaluation goes InProgress when the FIRST evaluator opens it -
        // so a state-only rule would close the second evaluator's declaration window before they had
        // one, and would reveal names to whoever happened to look first.
        //
        //   1. before this evaluator has declared, which is the recusal window BRULE-067 describes,
        //      served by the bidders route, and that route does NOT open scoring
        //   2. after consolidation, when the scores are in and locked, so a name cannot influence one
        var revealed = assignment.ConflictDeclaredAt is null
            || evaluation.State is EvaluationState.Consolidated or EvaluationState.Finalized;

        // Ordered by proposal code so the label for a given bid is the same on every read and for every
        // evaluator - a committee cannot discuss "Bidder B" if it means a different bid to each member.
        var labels = bids
            .OrderBy(b => b.ProposalCode, StringComparer.Ordinal)
            .Select((b, index) => (b.ProposalId, Index: index))
            .ToDictionary(x => x.ProposalId, x => x.Index);

        var proposals = bids.Select(b => new EvaluatorProposalDto(
            b.ProposalCode,
            BidderLabel.Arabic(labels[b.ProposalId]),
            BidderLabel.English(labels[b.ProposalId]),
            revealed ? b.SupplierReferenceCode : null,
            revealed ? b.SupplierDisplayNameAr : null,
            revealed ? b.SupplierDisplayNameEn : null,
            b.NarrativeAr, b.NarrativeEn, b.RequirementAnswers, b.Documents,
            evaluation.IsTechnicallyQualifiedByEvaluator(evaluatorUserId, b.ProposalId))).ToList();

        return new MyEvaluationDto(
            rfq.ReferenceCode, evaluation.State,
            rfq.TitleAr, rfq.TitleEn, rfq.DescriptionAr, rfq.DescriptionEn,
            [.. rfq.Items.OrderBy(i => i.LineNo).Select(i => new RfqItemDto(
                i.Id, i.LineNo, i.TitleAr, i.TitleEn, i.SpecificationAr, i.SpecificationEn,
                i.CategoryCode, i.Quantity, i.UnitOfMeasureCode, i.IsUnitPrice, i.IsOptional))],
            [.. rfq.Requirements.Select(r => new RequirementDto(r.Id, r.TextAr, r.TextEn, r.IsMandatory, r.DocumentTypeCode))],
            assignment.SubmittedAt, [.. evaluation.Criteria.Select(ToCriterionDto)], proposals, myScores);
    }
}
