// Turning an evaluation into the two read models it has: the committee's and one evaluator's workspace.
//
// Names and public bid codes are passed in rather than looked up, because this mapper is static and holds no
// database context. The alternative is a raw database identifier on the screen where a tender is decided,
// which is what a manager was actually reading before this was fixed.
//
// The bid codes used to be optional and six of the eight call sites left them out, so those responses carried
// nothing beside an identifier and the results table fell back to the identifier. An optional argument whose
// absence puts a database identifier on a manager's screen is not optional; it is a default that is wrong.
// It is required now.
//
// A result or a score whose bid row has gone is dropped rather than rendered with an invented code. It cannot
// happen, because results are written from live bids and nothing deletes them, and if it ever does, a missing
// row is a smaller lie than a made-up one.
//
//
// THE EVALUATOR'S WORKSPACE CANNOT REACH A PRICE
//
// The bids arrive already loaded and already narrowed to the technical envelope. This method does no querying
// at all, so there is no path by which a later edit could pull a pricing row in here.
//
//
// WHEN A BIDDER'S NAME IS SHOWN, AND WHY
//
// Bidders are anonymous while this evaluator is scoring, and named at the two moments where the name is the
// point.
//
// Before this evaluator has declared their conflicts, they are looking at an assignment they have been
// offered and deciding whether to recuse themselves, which is an assignment-time act that needs the names.
//
// After consolidation the scores are in and locked, so a name can no longer influence one.
//
// Between those two the name is withheld and a stable pseudonym stands in its place.
//
// The first window is keyed on THIS evaluator's own declaration rather than on the evaluation's state.
// Reading the workspace is itself what opens scoring, and the evaluation moves to in-progress when the FIRST
// evaluator opens it, so a state-only rule would close the second evaluator's declaration window before they
// had one, and would reveal the names to whoever happened to look first. The route that serves the declaration
// window does not open scoring.
//
// The pseudonyms are assigned in order of bid code, so a given bid wears the same label on every read and for
// every evaluator. A committee cannot discuss "bidder B" if it means a different bid to each member.

namespace MotsSupplierPortal.Infrastructure.Evaluation;

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

internal static class EvaluationDtoMapper
{
    public static EvaluationDto ToDto(
        EvaluationAggregate evaluation, Rfq rfq,
        IReadOnlyDictionary<Guid, string> proposalCodes,
        IReadOnlyDictionary<Guid, string>? names = null) => new(
        evaluation.Id, evaluation.RfqId, rfq.ReferenceCode, evaluation.State,
        [.. evaluation.Criteria.Select(ToCriterionDto)],
        [.. evaluation.Assignments.Select(a => new EvaluationAssignmentDto(
            a.EvaluatorUserId, names?.GetValueOrDefault(a.EvaluatorUserId), a.AssignedAt, a.SubmittedAt, a.RecusedAt, a.RecusalReason))],
        [.. evaluation.Results
            .Where(r => proposalCodes.ContainsKey(r.ProposalId))
            .Select(r => new ConsolidatedResultDto(
                r.ProposalId, proposalCodes[r.ProposalId], r.TechnicallyQualified,
                r.TechnicalWeightedScore, r.FinancialWeightedScore, r.WeightedTotal, r.Rank, r.TieUnresolved, r.TieResolutionReason))],
        evaluation.RowVersion);

    public static EvaluationCriterionDto ToCriterionDto(EvaluationCriterionSnapshot c) =>
        new(c.Id, c.NameAr, c.NameEn, c.Dimension, c.Weight, c.MaxScore, c.Threshold, c.ScoringType, c.IsFinancial,
            c.RequiresJustification, c.GuidanceAr, c.GuidanceEn);

    public static MyEvaluationDto ToMyDto(
        EvaluationAggregate evaluation, Rfq rfq, Guid evaluatorUserId, IReadOnlyList<EvaluatorBid> bids)
    {
        var assignment = evaluation.Assignments.First(a => a.EvaluatorUserId == evaluatorUserId && a.IsActive);
        var codeById = bids.ToDictionary(b => b.ProposalId, b => b.ProposalCode);

        var myScores = evaluation.Scores.Where(s => s.EvaluatorUserId == evaluatorUserId)
            .Where(s => codeById.ContainsKey(s.ProposalId))
            .Select(s => new MyScoreDto(codeById[s.ProposalId], s.CriterionId, s.RawScore, s.CommentAr, s.CommentEn, s.ScoredAt))
            .ToList();

        var revealed = assignment.ConflictDeclaredAt is null
            || evaluation.State is EvaluationState.Consolidated or EvaluationState.Finalized;

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
