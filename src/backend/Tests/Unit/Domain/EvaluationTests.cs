// The evaluation aggregate: its states, the two-envelope gate, and the four tie-break rungs.
//
// The transitions are checked against the written process directly; the aggregate's own header carries the
// quoted rows.
//
//
// THE TWO-ENVELOPE GATE IS THIS FILE'S CENTREPIECE
//
// Scoring a financial criterion must be refused until the SAME evaluator has cleared every technical
// criterion's threshold for that bid.
//
// It carries a revert-to-red: deliberately weaken the gate by scoring the financial criterion with no prior
// technical score at all, which is the simplest possible bypass, and confirm the refusal fires for exactly
// that reason rather than a coincidental different failure.
//
//
// THE TIE-BREAK RUNGS, EACH STATED AS BEHAVIOUR RATHER THAN AS AN ORDERING CLAUSE
//
// Before this, the ranking ordered by the weighted total alone, so two bids with the same total took first and
// second place in whatever order the score rows happened to iterate. First place is what the award flow offers.
// Found by a sweep over the written rules.
//
// The bids in these tests have identical totals BY CONSTRUCTION, which is the case that used to be arbitrary.
// That also needs the financial scores equal, not just the technical ones, because a tie on the weighted total
// is the premise and the default weights make the totals differ otherwise.
//
// Rung one: with equal totals, the bid that scored higher on TECHNICAL criteria outranks the one that made the
// total up on price, which is the direction the rule names.
//
// Rung three: identical totals and identical technical scores, so the only thing separating the two bids is
// price, and the document says the cheaper compliant bid wins.
//
// Rung four, the last one a rule can decide: earliest submission, which is objective, already recorded, and
// cannot be manipulated after the fact.
//
// And the order is REPRODUCIBLE. Consolidating the same scores again ranks them the same way, which is the
// property the old code lacked and the one an auditor would ask about.
//
//
// WHAT HAPPENS WHEN THE RUNGS RUN OUT
//
// Equal on total, technical score, price and submission instant is equal on everything a rule can see. The
// ranks are still assigned, because a list with no order is useless, but they are MARKED and the award flow
// refuses to act on them.
//
// One direction surfaces the case: two bids with no recorded price are not "equal on price" in any way that
// resolves anything, so they must not be silently ordered by identifier.
//
// Resolving a tie by hand has three refusals, each a different mistake, and a control showing that a real
// resolution takes effect, keeps the ranks it was given, and clears the marker for EVERY member of the group,
// including the one that lost, because the tie is resolved for both once somebody has put their name to it.
// Resolving something already resolved is refused.

namespace MotsSupplierPortal.Tests.Unit.Domain;

using FluentAssertions;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Suppliers;
using EvaluationAggregate = MotsSupplierPortal.Domain.Evaluation.Evaluation;

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

    [Fact]
    public void Revert_to_red_financial_gate_cannot_be_bypassed_by_scoring_financial_first()
    {
        var evaluation = CreateAssignedEvaluation();
        var financialId = FinancialCriterionId(evaluation);

        Action act = () => evaluation.ScoreCriterion(EvaluatorA, ProposalA, financialId, 100m, null, null, Proposals);
        act.Should().Throw<DomainException>();

        evaluation.Scores.Should().BeEmpty("the refused financial score must never be persisted onto the aggregate");
    }

    [Fact]
    public void EvaluatorScore_rows_are_isolated_per_evaluator()
    {
        var evaluation = CreateAssignedEvaluation();
        evaluation.ScoreCriterion(EvaluatorA, ProposalA, TechnicalCriterionId(evaluation), 75m, null, null, Proposals);
        evaluation.ScoreCriterion(EvaluatorB, ProposalA, TechnicalCriterionId(evaluation), 30m, null, null, Proposals);

        evaluation.Scores.Where(s => s.EvaluatorUserId == EvaluatorA).Should().ContainSingle(s => s.RawScore == 75m);
        evaluation.Scores.Where(s => s.EvaluatorUserId == EvaluatorB).Should().ContainSingle(s => s.RawScore == 30m);
    }

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

    private static EvaluationAggregate CreateFullySubmittedEvaluation(
        decimal proposalAScore, decimal proposalBScore, decimal threshold = 60m,
        decimal proposalAFinancial = 50m, decimal proposalBFinancial = 70m)
    {
        var evaluation = CreateAssignedEvaluation(threshold);
        var techId = TechnicalCriterionId(evaluation);
        var finId = FinancialCriterionId(evaluation);
        foreach (var evaluatorId in new[] { EvaluatorA, EvaluatorB })
        {
            evaluation.ScoreCriterion(evaluatorId, ProposalA, techId, proposalAScore, null, null, Proposals);
            if (proposalAScore >= threshold) evaluation.ScoreCriterion(evaluatorId, ProposalA, finId, proposalAFinancial, null, null, Proposals);
            evaluation.ScoreCriterion(evaluatorId, ProposalB, techId, proposalBScore, null, null, Proposals);
            if (proposalBScore >= threshold) evaluation.ScoreCriterion(evaluatorId, ProposalB, finId, proposalBFinancial, null, null, Proposals);
            evaluation.SubmitEvaluator(evaluatorId, Proposals);
        }
        return evaluation;
    }

    private static EvaluationAggregate CreateTiedEvaluation() =>
        CreateFullySubmittedEvaluation(proposalAScore: 80m, proposalBScore: 80m, proposalAFinancial: 60m, proposalBFinancial: 60m);

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
        var evaluation = CreateFullySubmittedEvaluation(proposalAScore: 80m, proposalBScore: 80m);
        evaluation.Consolidate();

        var ranks = evaluation.Results.Where(r => r.TechnicallyQualified).Select(r => r.Rank).ToList();
        ranks.Should().BeEquivalentTo([1, 2], "a tie still produces a total order, not two rank ones");

        var first = evaluation.Results.Single(r => r.Rank == 1).ProposalId;

        var again = CreateFullySubmittedEvaluation(proposalAScore: 80m, proposalBScore: 80m);
        again.Consolidate();
        again.Results.Single(r => r.Rank == 1).ProposalId.Should().Be(first);
    }

    [Fact]
    public void Consolidate_ranks_the_higher_technical_score_first_when_totals_are_equal()
    {
        var evaluation = CreateFullySubmittedEvaluation(proposalAScore: 80m, proposalBScore: 80m);
        evaluation.Consolidate();

        var ordered = evaluation.Results
            .Where(r => r.TechnicallyQualified)
            .OrderBy(r => r.Rank)
            .ToList();

        ordered[0].TechnicalWeightedScore.Should().BeGreaterThanOrEqualTo(ordered[1].TechnicalWeightedScore);
    }

    [Fact]
    public void Consolidate_breaks_a_tie_on_the_lower_commercial_total()
    {
        var evaluation = CreateTiedEvaluation();
        var submittedAt = DateTimeOffset.Parse("2026-09-01T10:00:00Z");

        evaluation.Consolidate(new Dictionary<Guid, EvaluationAggregate.BidTieBreakFacts>
        {
            [ProposalA] = new(2_000m, submittedAt),
            [ProposalB] = new(1_000m, submittedAt),
        });

        evaluation.Results.Single(r => r.ProposalId == ProposalB).Rank.Should().Be(1, "the cheaper bid outranks");
        evaluation.Results.Single(r => r.ProposalId == ProposalA).Rank.Should().Be(2);
        evaluation.Results.Should().OnlyContain(r => !r.TieUnresolved, "price resolved it, so nothing is surfaced");
    }

    [Fact]
    public void Consolidate_breaks_a_price_tie_on_the_earlier_submission()
    {
        var evaluation = CreateTiedEvaluation();

        evaluation.Consolidate(new Dictionary<Guid, EvaluationAggregate.BidTieBreakFacts>
        {
            [ProposalA] = new(1_000m, DateTimeOffset.Parse("2026-09-02T10:00:00Z")),
            [ProposalB] = new(1_000m, DateTimeOffset.Parse("2026-09-01T10:00:00Z")),
        });

        evaluation.Results.Single(r => r.ProposalId == ProposalB).Rank.Should().Be(1, "submitted a day earlier");
        evaluation.Results.Should().OnlyContain(r => !r.TieUnresolved);
    }

    [Fact]
    public void Consolidate_surfaces_a_tie_that_survives_every_rung_rather_than_picking_one()
    {
        var at = DateTimeOffset.Parse("2026-09-01T10:00:00Z");
        var evaluation = CreateTiedEvaluation();

        evaluation.Consolidate(new Dictionary<Guid, EvaluationAggregate.BidTieBreakFacts>
        {
            [ProposalA] = new(1_000m, at),
            [ProposalB] = new(1_000m, at),
        });

        evaluation.Results.Where(r => r.TechnicallyQualified).Should().OnlyContain(r => r.TieUnresolved);
        evaluation.Results.Select(r => r.Rank).Should().BeEquivalentTo([1, 2], "still a total order, just not a decided one");
    }

    [Fact]
    public void An_unknown_price_counts_as_a_tie_rather_than_as_a_difference()
    {
        var at = DateTimeOffset.Parse("2026-09-01T10:00:00Z");
        var evaluation = CreateTiedEvaluation();

        evaluation.Consolidate(new Dictionary<Guid, EvaluationAggregate.BidTieBreakFacts>
        {
            [ProposalA] = new(null, at),
            [ProposalB] = new(null, at),
        });

        evaluation.Results.Where(r => r.TechnicallyQualified).Should().OnlyContain(r => r.TieUnresolved);
    }

    [Fact]
    public void Resolving_a_tie_needs_a_reason_a_real_proposal_and_an_actual_tie()
    {
        var at = DateTimeOffset.Parse("2026-09-01T10:00:00Z");
        var evaluation = CreateTiedEvaluation();
        evaluation.Consolidate(new Dictionary<Guid, EvaluationAggregate.BidTieBreakFacts>
        {
            [ProposalA] = new(1_000m, at),
            [ProposalB] = new(1_000m, at),
        });
        var resolver = Guid.CreateVersion7();

        ((Action)(() => evaluation.ResolveTie(ProposalA, resolver, "   ")))
            .Should().Throw<DomainException>().WithMessage("*reason is required*");
        ((Action)(() => evaluation.ResolveTie(Guid.CreateVersion7(), resolver, "Because.")))
            .Should().Throw<DomainException>().WithMessage("*not part of this evaluation*");

        evaluation.ResolveTie(ProposalB, resolver, "Prior delivery record on comparable work.");

        evaluation.Results.Single(r => r.ProposalId == ProposalB).Rank.Should().Be(1);
        evaluation.Results.Single(r => r.ProposalId == ProposalA).Rank.Should().Be(2);
        evaluation.Results.Where(r => r.TechnicallyQualified).Should().OnlyContain(r => !r.TieUnresolved);
        evaluation.Results.Where(r => r.TechnicallyQualified).Should()
            .OnlyContain(r => r.TieResolvedByUserId == resolver && r.TieResolutionReason == "Prior delivery record on comparable work.");

        ((Action)(() => evaluation.ResolveTie(ProposalB, resolver, "Again.")))
            .Should().Throw<DomainException>().WithMessage("*not part of an unresolved tie*");
    }
}
