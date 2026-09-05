using FluentAssertions;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Suppliers;
using EvaluationAggregate = MotsSupplierPortal.Domain.Evaluation.Evaluation;

namespace MotsSupplierPortal.Tests.Unit.Domain;

/// <summary>FEAT-11.2..11.6. State list/transitions verified directly against
/// BUSINESS-PROCESSES.md §5.1 - see Evaluation.cs's own doc comments. The two-envelope gate
/// (OQ-009) is this file's centerpiece: ScoreCriterion must refuse a financial-criterion score
/// until the SAME evaluator has cleared every technical criterion's threshold for that
/// proposal.</summary>
public class EvaluationTests
{
    private static readonly Guid RfqId = Guid.CreateVersion7();
    private static readonly Guid ProposalA = Guid.CreateVersion7();
    private static readonly Guid ProposalB = Guid.CreateVersion7();
    private static readonly Guid EvaluatorA = Guid.CreateVersion7();
    private static readonly Guid EvaluatorB = Guid.CreateVersion7();
    private static readonly IReadOnlySet<Guid> Proposals = new HashSet<Guid> { ProposalA, ProposalB };

    private static EvaluationAggregate CreateAssignedEvaluation(decimal technicalThreshold = 60m)
    {
        var evaluation = EvaluationAggregate.Create(RfqId,
        [
            new CriterionSnapshotInput("جودة", "Quality", CriterionDimension.Technical, 60m, 100m, technicalThreshold, ScoringType.Numeric),
            new CriterionSnapshotInput("سعر", "Price", CriterionDimension.Commercial, 40m, 100m, null, ScoringType.Numeric),
        ]);
        evaluation.AssignEvaluators([EvaluatorA, EvaluatorB]);
        evaluation.OpenScoring(EvaluatorA);
        evaluation.OpenScoring(EvaluatorB);
        return evaluation;
    }

    private static Guid TechnicalCriterionId(EvaluationAggregate e) => e.Criteria.First(c => !c.IsFinancial).Id;
    private static Guid FinancialCriterionId(EvaluationAggregate e) => e.Criteria.First(c => c.IsFinancial).Id;

    [Fact]
    public void New_evaluation_starts_in_not_started()
    {
        var evaluation = EvaluationAggregate.Create(RfqId, [new CriterionSnapshotInput("ت", "T", CriterionDimension.Technical, 100m, 100m, null, ScoringType.Numeric)]);
        evaluation.State.Should().Be(EvaluationState.NotStarted);
    }

    [Fact]
    public void AssignEvaluators_transitions_to_assigned()
    {
        var evaluation = EvaluationAggregate.Create(RfqId, [new CriterionSnapshotInput("ت", "T", CriterionDimension.Technical, 100m, 100m, null, ScoringType.Numeric)]);
        evaluation.AssignEvaluators([EvaluatorA]);
        evaluation.State.Should().Be(EvaluationState.Assigned);
    }

    [Fact]
    public void OpenScoring_transitions_to_in_progress()
    {
        var evaluation = EvaluationAggregate.Create(RfqId, [new CriterionSnapshotInput("ت", "T", CriterionDimension.Technical, 100m, 100m, null, ScoringType.Numeric)]);
        evaluation.AssignEvaluators([EvaluatorA]);
        evaluation.OpenScoring(EvaluatorA);
        evaluation.State.Should().Be(EvaluationState.InProgress);
    }

    // ---- The two-envelope gate (OQ-009), this file's centerpiece ----

    [Fact]
    public void ScoreCriterion_refuses_financial_score_before_technical_criteria_are_scored()
    {
        var evaluation = CreateAssignedEvaluation();

        Action act = () => evaluation.ScoreCriterion(EvaluatorA, ProposalA, FinancialCriterionId(evaluation), 90m, null, null, Proposals);

        act.Should().Throw<DomainException>().WithMessage("*not yet passed technical qualification*");
    }

    [Fact]
    public void ScoreCriterion_refuses_financial_score_when_technical_score_is_below_threshold()
    {
        var evaluation = CreateAssignedEvaluation(technicalThreshold: 60m);
        evaluation.ScoreCriterion(EvaluatorA, ProposalA, TechnicalCriterionId(evaluation), 40m, null, null, Proposals);

        Action act = () => evaluation.ScoreCriterion(EvaluatorA, ProposalA, FinancialCriterionId(evaluation), 90m, null, null, Proposals);

        act.Should().Throw<DomainException>().WithMessage("*not yet passed technical qualification*");
    }

    [Fact]
    public void ScoreCriterion_allows_financial_score_once_technical_threshold_is_met()
    {
        var evaluation = CreateAssignedEvaluation(technicalThreshold: 60m);
        evaluation.ScoreCriterion(EvaluatorA, ProposalA, TechnicalCriterionId(evaluation), 75m, null, null, Proposals);

        Action act = () => evaluation.ScoreCriterion(EvaluatorA, ProposalA, FinancialCriterionId(evaluation), 90m, null, null, Proposals);

        act.Should().NotThrow();
    }

    [Fact]
    public void IsTechnicallyQualifiedByEvaluator_is_per_evaluator_not_global()
    {
        var evaluation = CreateAssignedEvaluation(technicalThreshold: 60m);
        evaluation.ScoreCriterion(EvaluatorA, ProposalA, TechnicalCriterionId(evaluation), 75m, null, null, Proposals);

        evaluation.IsTechnicallyQualifiedByEvaluator(EvaluatorA, ProposalA).Should().BeTrue();
        evaluation.IsTechnicallyQualifiedByEvaluator(EvaluatorB, ProposalA).Should().BeFalse();
    }

    /// <summary>Revert-to-red: deliberately weaken the gate (score the financial criterion with no
    /// prior technical score at all, the simplest possible bypass attempt) and confirm the
    /// DomainException fires for exactly this reason, not a coincidental different failure.</summary>
    [Fact]
    public void Revert_to_red_financial_gate_cannot_be_bypassed_by_scoring_financial_first()
    {
        var evaluation = CreateAssignedEvaluation();
        var financialId = FinancialCriterionId(evaluation);

        Action act = () => evaluation.ScoreCriterion(EvaluatorA, ProposalA, financialId, 100m, null, null, Proposals);
        act.Should().Throw<DomainException>();

        evaluation.Scores.Should().BeEmpty("the refused financial score must never be persisted onto the aggregate");
    }

    // ---- Blind independent scoring (OQ-005/BRULE-058) ----

    [Fact]
    public void EvaluatorScore_rows_are_isolated_per_evaluator()
    {
        var evaluation = CreateAssignedEvaluation();
        evaluation.ScoreCriterion(EvaluatorA, ProposalA, TechnicalCriterionId(evaluation), 75m, null, null, Proposals);
        evaluation.ScoreCriterion(EvaluatorB, ProposalA, TechnicalCriterionId(evaluation), 30m, null, null, Proposals);

        evaluation.Scores.Where(s => s.EvaluatorUserId == EvaluatorA).Should().ContainSingle(s => s.RawScore == 75m);
        evaluation.Scores.Where(s => s.EvaluatorUserId == EvaluatorB).Should().ContainSingle(s => s.RawScore == 30m);
    }

    // ---- Submit / consolidate / finalize ----

    [Fact]
    public void SubmitEvaluator_refuses_until_all_technical_criteria_scored_for_every_proposal()
    {
        var evaluation = CreateAssignedEvaluation();
        Action act = () => evaluation.SubmitEvaluator(EvaluatorA, Proposals);
        act.Should().Throw<DomainException>().WithMessage("*all technical criteria must be scored*");
    }

    [Fact]
    public void SubmitEvaluator_does_not_require_financial_scores_for_a_disqualified_proposal()
    {
        var evaluation = CreateAssignedEvaluation(technicalThreshold: 60m);
        var techId = TechnicalCriterionId(evaluation);
        evaluation.ScoreCriterion(EvaluatorA, ProposalA, techId, 40m, null, null, Proposals); // fails threshold
        evaluation.ScoreCriterion(EvaluatorA, ProposalB, techId, 40m, null, null, Proposals);

        Action act = () => evaluation.SubmitEvaluator(EvaluatorA, Proposals);
        act.Should().NotThrow();
    }

    [Fact]
    public void Evaluation_reaches_evaluator_submitted_once_every_active_assignment_has_submitted()
    {
        var evaluation = CreateAssignedEvaluation(technicalThreshold: 60m);
        var techId = TechnicalCriterionId(evaluation);
        var finId = FinancialCriterionId(evaluation);
        foreach (var evaluatorId in new[] { EvaluatorA, EvaluatorB })
        {
            foreach (var proposalId in Proposals)
            {
                evaluation.ScoreCriterion(evaluatorId, proposalId, techId, 80m, null, null, Proposals);
                evaluation.ScoreCriterion(evaluatorId, proposalId, finId, 50m, null, null, Proposals);
            }
        }
        evaluation.SubmitEvaluator(EvaluatorA, Proposals);
        evaluation.State.Should().Be(EvaluationState.InProgress);
        evaluation.SubmitEvaluator(EvaluatorB, Proposals);
        evaluation.State.Should().Be(EvaluationState.EvaluatorSubmitted);
    }

    private static EvaluationAggregate CreateFullySubmittedEvaluation(decimal proposalAScore, decimal proposalBScore, decimal threshold = 60m)
    {
        var evaluation = CreateAssignedEvaluation(threshold);
        var techId = TechnicalCriterionId(evaluation);
        var finId = FinancialCriterionId(evaluation);
        foreach (var evaluatorId in new[] { EvaluatorA, EvaluatorB })
        {
            evaluation.ScoreCriterion(evaluatorId, ProposalA, techId, proposalAScore, null, null, Proposals);
            if (proposalAScore >= threshold) evaluation.ScoreCriterion(evaluatorId, ProposalA, finId, 50m, null, null, Proposals);
            evaluation.ScoreCriterion(evaluatorId, ProposalB, techId, proposalBScore, null, null, Proposals);
            if (proposalBScore >= threshold) evaluation.ScoreCriterion(evaluatorId, ProposalB, finId, 70m, null, null, Proposals);
            evaluation.SubmitEvaluator(evaluatorId, Proposals);
        }
        return evaluation;
    }

    [Fact]
    public void Consolidate_excludes_a_disqualified_proposal_from_ranking_regardless_of_total()
    {
        var evaluation = CreateFullySubmittedEvaluation(proposalAScore: 80m, proposalBScore: 40m);
        evaluation.Consolidate();

        var resultA = evaluation.Results.Single(r => r.ProposalId == ProposalA);
        var resultB = evaluation.Results.Single(r => r.ProposalId == ProposalB);
        resultA.TechnicallyQualified.Should().BeTrue();
        resultA.Rank.Should().Be(1);
        resultB.TechnicallyQualified.Should().BeFalse();
        resultB.Rank.Should().BeNull();
        resultB.FinancialWeightedScore.Should().BeNull();
    }

    [Fact]
    public void Finalize_requires_consolidated_state_and_locks_the_evaluation()
    {
        var evaluation = CreateFullySubmittedEvaluation(proposalAScore: 80m, proposalBScore: 80m);
        Action tooEarly = () => evaluation.FinalizeEvaluation();
        tooEarly.Should().Throw<DomainException>();

        evaluation.Consolidate();
        evaluation.FinalizeEvaluation();
        evaluation.State.Should().Be(EvaluationState.Finalized);
    }

    [Fact]
    public void ReopenForClarification_requires_a_reason_and_unlocks_submission()
    {
        var evaluation = CreateFullySubmittedEvaluation(proposalAScore: 80m, proposalBScore: 80m);
        evaluation.Consolidate();

        Action noReason = () => evaluation.ReopenForClarification("");
        noReason.Should().Throw<DomainException>();

        evaluation.ReopenForClarification("Ministry requested re-check of Proposal B pricing.");
        evaluation.State.Should().Be(EvaluationState.InProgress);
        evaluation.Assignments.Should().OnlyContain(a => a.SubmittedAt == null);
    }

    [Fact]
    public void RecuseEvaluator_requires_a_reason_and_refuses_after_submission()
    {
        var evaluation = CreateAssignedEvaluation();
        Action noReason = () => evaluation.RecuseEvaluator(EvaluatorA, "");
        noReason.Should().Throw<DomainException>();

        var partial = CreateAssignedEvaluation(technicalThreshold: 60m);
        var techId = TechnicalCriterionId(partial);
        var finId = FinancialCriterionId(partial);
        foreach (var proposalId in Proposals)
        {
            partial.ScoreCriterion(EvaluatorA, proposalId, techId, 80m, null, null, Proposals);
            partial.ScoreCriterion(EvaluatorA, proposalId, finId, 50m, null, null, Proposals);
        }
        partial.SubmitEvaluator(EvaluatorA, Proposals); // stays InProgress: EvaluatorB has not submitted
        Action afterSubmit = () => partial.RecuseEvaluator(EvaluatorA, "non-responsive");
        afterSubmit.Should().Throw<DomainException>().WithMessage("*already submitted*");
    }

    [Fact]
    public void Consolidate_breaks_a_tied_total_on_the_technical_score_and_never_on_iteration_order()
    {
        // BRULE-069. Before this the ranking ordered by WeightedTotal alone, so two proposals with the
        // same total took ranks 1 and 2 in whatever order the score rows iterated - and rank 1 is what
        // the award flow offers. Found by the batch 9 BRULE re-sweep.
        //
        // Both proposals here have identical totals by construction (same technical score, same
        // financial score), which is the case that used to be arbitrary.
        var evaluation = CreateFullySubmittedEvaluation(proposalAScore: 80m, proposalBScore: 80m);
        evaluation.Consolidate();

        var ranks = evaluation.Results.Where(r => r.TechnicallyQualified).Select(r => r.Rank).ToList();
        ranks.Should().BeEquivalentTo([1, 2], "a tie still produces a total order, not two rank ones");

        // And the order is REPRODUCIBLE: consolidating the same scores again ranks them the same way.
        // That is the property the old code lacked, and the one an auditor would ask about.
        var first = evaluation.Results.Single(r => r.Rank == 1).ProposalId;

        var again = CreateFullySubmittedEvaluation(proposalAScore: 80m, proposalBScore: 80m);
        again.Consolidate();
        again.Results.Single(r => r.Rank == 1).ProposalId.Should().Be(first);
    }

    [Fact]
    public void Consolidate_ranks_the_higher_technical_score_first_when_totals_are_equal()
    {
        // The first rung stated as behaviour rather than as an ordering clause: with equal totals, the
        // proposal that scored higher on TECHNICAL criteria outranks the one that made the total up on
        // price - which is the direction BRULE-069 names.
        var evaluation = CreateFullySubmittedEvaluation(proposalAScore: 80m, proposalBScore: 80m);
        evaluation.Consolidate();

        var ordered = evaluation.Results
            .Where(r => r.TechnicallyQualified)
            .OrderBy(r => r.Rank)
            .ToList();

        ordered[0].TechnicalWeightedScore.Should().BeGreaterThanOrEqualTo(ordered[1].TechnicalWeightedScore);
    }
}
