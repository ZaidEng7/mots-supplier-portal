// The scoring of one tender's submitted bids: the committee, their scores, and the consolidated
// result. It is its own record, linked to the tender by id rather than being part of it, the same
// shape a bid has.
//
// CriterionSnapshotInput is what Create is given: one criterion as it stood on the tender's frozen
// template. The guidance is optional with a default so existing callers and tests that do not care
// about it are unchanged, but the one caller that binds a real template must pass it, and a test
// asserts that.
//
//
// THE TWO-ENVELOPE GATE, which is the centre of this design
//
// A bid's pricing is stored in a genuinely separate table from its technical content. This record is
// what makes that separation matter in practice.
//
// ScoreCriterion refuses a score for a commercial criterion on a bid until the same evaluator has
// scored every technical criterion for that bid and none has fallen below its threshold.
//
// It is enforced per evaluator rather than across the committee, because scoring is blind and
// independent: one evaluator's technical judgement on a bid must not be influenced by whether another
// has already opened its pricing, and the gate has to hold in the middle of scoring, long before
// anything is consolidated.
//
// Consolidate then re-derives technical qualification from the averaged scores, and that is the
// authoritative final answer. A bid that fails it is excluded from the ranking regardless of its
// total. The per-evaluator gate during scoring and the consolidated gate at the end are the same rule
// applied at two moments, not two different rules.
//
//
// THE LIFECYCLE
//
// Create takes the frozen criteria and produces an evaluation that has not started. The guidance text
// is copied along with the weights, because an evaluator must see the instruction that was in force
// when the tender bound the template rather than whatever it says now.
//
// AssignEvaluators moves it to assigned on the first call and can be called again later to add more
// evaluators. That is also the real tool for replacing an evaluator who has gone quiet: recuse the old
// one and assign a new one, rather than inventing a separate reassignment action.
//
// OpenScoring moves it to in progress when the first evaluator opens it, and changes nothing for
// everyone after that.
//
// ScoreCriterion records or replaces one score. The bid must be one of the tender's submitted bids;
// the handler passes that set in, because bids live elsewhere and this record should not trust an
// arbitrary identifier. The score must be between zero and the criterion's maximum.
//
// A criterion that requires a justification needs a comment in either language, not both. An evaluator
// writes their reasoning in the language they think in, and demanding a translation from the person
// making the judgement would either produce a machine-translated second copy or stop the score being
// recorded at all. That is different from a supplier-facing field, where both languages are the
// product: this comment is internal evidence for a procurement file, read by the committee that wrote
// it and by an auditor afterwards.
//
// SubmitEvaluator locks one evaluator's work. "Fully scored" means every technical criterion on every
// bid, plus every financial criterion on every bid that passed technical qualification for this
// evaluator. A disqualified bid legitimately never needs its financial criteria scored, so it is not
// held against submission. When the last active evaluator submits, the evaluation moves on.
//
// DeclareNoConflict records that this evaluator has seen the bidder list and declared no conflict. A
// declaration is made once and cannot be remade, because the point of the window is that it closes. A
// conflicted evaluator does not call this; they are recused, which is recorded and audited.
//
// RecuseEvaluator steps an evaluator aside with a mandatory reason. It is refused once they have
// submitted, because a submitted evaluator's scores are locked and recusing them afterwards would
// silently discard a real input. Recusing the last outstanding evaluator can complete the round, which
// is why the state is re-checked here too.
//
// Consolidate averages each criterion's scores across evaluators, divides by the maximum, multiplies
// by the weight and sums. Averaging is the default the written rule states while leaving the policy
// open to confirmation.
//
// FinalizeEvaluation settles the result. The written rule also asks for "no unresolved clarification",
// and that half is not checked here, because clarifications are a different record this one does not
// own.
//
// ReopenForClarification sends a consolidated evaluation back to in progress with a mandatory reason,
// clears the results and unlocks every active evaluator's submission. The written rule says to unlock
// "affected" assignments and never defines which those are, so this unlocks all of them rather than
// inventing the rule.
//
//
// QUORUM, deliberately not built
//
// The written rules tag quorum policy as needing business confirmation and name no number.
// Consolidation therefore requires every actively assigned evaluator to have submitted, and there is
// no partial-quorum path. A manager's actual tool for an evaluator who never responds is recusal,
// reused rather than inventing a second exclude-for-non-response action: removing them from the active
// set changes what "every active evaluator" means, without fabricating a quorum fraction.
//
//
// TIE-BREAKING
//
// BidTieBreakFacts carries the two facts a tie-break needs that the scores do not: the bid's own
// priced total and the moment it was submitted. Both are passed in rather than read here, because this
// record has no access to bids, and inferring "cheapest" from the financial score would assume that
// score runs inverse to price, which no document states.
//
// The rungs are, in order: highest weighted total, then highest technical score, then lowest priced
// total, then earliest submission.
//
// Ordering by the total alone, which is what this used to do, left ties resolved by whatever order the
// score rows happened to come out in, so two bids with identical totals took first and second place
// arbitrarily, and first place is what the award flow offers. In a government tender that is the
// ordering that gets challenged, and nothing in the record would explain it.
//
// A bid with no priced total sorts last on the price rung, because it cannot claim to be the cheapest.
//
// Earliest submission is the last rung because it is objective, already recorded and cannot be
// manipulated afterwards, which is why it is the standard final rung in public procurement. The bid's
// identifier is used after that only so the list is stable across re-consolidations, and a tie that
// reaches it is not treated as resolved.
//
// A tie that survives every rung is surfaced rather than picked. Two bids equal on total, technical
// score, price and submission instant are equal on everything a rule can see, and the ordering between
// them came from an identifier, which is not a decision anybody made. A genuine full tie is rare
// enough that a day of manual resolution costs less than a challenge to a silently picked winner.
//
// IsFullTie treats an unknown price or an unknown submission time as a tie rather than as a
// difference. Two bids with no recorded price are not meaningfully equal on that rung, so the unknown
// surfaces the case instead of inventing an order.
//
// ResolveTie is a person breaking a tie the rules could not, with an audited reason. The chosen bid
// takes the best rank held by any member of its tie group and the others follow in their existing
// order. Every member's marker clears, including the ones that lost, because the tie is resolved once
// somebody has put their name to it. The group is every unresolved result sharing this one's total and
// technical score; price and submission are not re-compared, because they were equal by construction,
// which is what set the marker in the first place.

namespace MotsSupplierPortal.Domain.Evaluation;

using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Suppliers;

public sealed record CriterionSnapshotInput(
    string NameAr, string NameEn, CriterionDimension Dimension, decimal Weight, decimal MaxScore, decimal? Threshold,
    ScoringType ScoringType, bool RequiresJustification = false,
    string? GuidanceAr = null, string? GuidanceEn = null);

public sealed class Evaluation : IVersionedAggregate
{
    private readonly List<EvaluationCriterionSnapshot> _criteria = [];
    private readonly List<EvaluationAssignment> _assignments = [];
    private readonly List<EvaluatorScore> _scores = [];
    private readonly List<ConsolidatedResult> _results = [];

    public Guid Id { get; private init; }
    public Guid RfqId { get; private init; }
    public EvaluationState State { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }
    public uint RowVersion { get; private set; }

    public IReadOnlyList<EvaluationCriterionSnapshot> Criteria => _criteria;
    public IReadOnlyList<EvaluationAssignment> Assignments => _assignments;
    public IReadOnlyList<EvaluatorScore> Scores => _scores;
    public IReadOnlyList<ConsolidatedResult> Results => _results;

    private Evaluation() { }

    public static Evaluation Create(Guid rfqId, IReadOnlyList<CriterionSnapshotInput> criteria)
    {
        if (criteria.Count == 0) throw new DomainException("Cannot create an evaluation with no criteria.");

        var evaluation = new Evaluation
        {
            Id = Guid.CreateVersion7(),
            RfqId = rfqId,
            State = EvaluationState.NotStarted,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        foreach (var c in criteria)
        {
            evaluation._criteria.Add(new EvaluationCriterionSnapshot
            {
                Id = Guid.CreateVersion7(),
                EvaluationId = evaluation.Id,
                NameAr = c.NameAr,
                NameEn = c.NameEn,
                Dimension = c.Dimension,
                Weight = c.Weight,
                MaxScore = c.MaxScore,
                Threshold = c.Threshold,
                ScoringType = c.ScoringType,
                RequiresJustification = c.RequiresJustification,
                GuidanceAr = c.GuidanceAr,
                GuidanceEn = c.GuidanceEn,
            });
        }
        return evaluation;
    }

    public void AssignEvaluators(IReadOnlyList<Guid> evaluatorUserIds)
    {
        if (State is EvaluationState.EvaluatorSubmitted or EvaluationState.Consolidated or EvaluationState.Finalized)
        {
            throw new DomainException($"Cannot assign evaluators from state '{State}'.");
        }
        if (evaluatorUserIds.Count == 0) throw new DomainException("At least one evaluator must be assigned.");

        foreach (var evaluatorUserId in evaluatorUserIds)
        {
            if (_assignments.Any(a => a.EvaluatorUserId == evaluatorUserId && a.IsActive))
            {
                throw new DomainException("This evaluator is already assigned.");
            }
            _assignments.Add(new EvaluationAssignment
            {
                Id = Guid.CreateVersion7(),
                EvaluationId = Id,
                EvaluatorUserId = evaluatorUserId,
                AssignedAt = DateTimeOffset.UtcNow,
            });
        }

        if (State == EvaluationState.NotStarted) State = EvaluationState.Assigned;
    }

    private EvaluationAssignment ActiveAssignment(Guid evaluatorUserId) =>
        _assignments.FirstOrDefault(a => a.EvaluatorUserId == evaluatorUserId && a.IsActive)
        ?? throw new DomainException("This evaluator is not assigned to this evaluation.");

    public void OpenScoring(Guid evaluatorUserId)
    {
        ActiveAssignment(evaluatorUserId);
        if (State is not (EvaluationState.Assigned or EvaluationState.InProgress))
        {
            throw new DomainException($"Cannot open scoring from state '{State}'; only 'Assigned' or 'InProgress' is valid.");
        }
        State = EvaluationState.InProgress;
    }

    private bool AllTechnicalCriteriaScored(Guid evaluatorUserId, Guid proposalId) =>
        _criteria.Where(c => !c.IsFinancial)
            .All(c => _scores.Any(s => s.EvaluatorUserId == evaluatorUserId && s.ProposalId == proposalId && s.CriterionId == c.Id));

    public bool IsTechnicallyQualifiedByEvaluator(Guid evaluatorUserId, Guid proposalId)
    {
        var technicalCriteria = _criteria.Where(c => !c.IsFinancial).ToList();
        foreach (var criterion in technicalCriteria)
        {
            var score = _scores.FirstOrDefault(s => s.EvaluatorUserId == evaluatorUserId && s.ProposalId == proposalId && s.CriterionId == criterion.Id);
            if (score is null) return false;
            if (criterion.Threshold is not null && score.RawScore < criterion.Threshold) return false;
        }
        return true;
    }

    public void ScoreCriterion(Guid evaluatorUserId, Guid proposalId, Guid criterionId, decimal rawScore, string? commentAr, string? commentEn, IReadOnlySet<Guid> validProposalIds)
    {
        if (State != EvaluationState.InProgress)
        {
            throw new DomainException($"Cannot score from state '{State}'; only 'InProgress' is valid.");
        }
        ActiveAssignment(evaluatorUserId);
        if (!validProposalIds.Contains(proposalId))
        {
            throw new DomainException("This proposal is not part of this evaluation.");
        }
        var criterion = _criteria.FirstOrDefault(c => c.Id == criterionId)
            ?? throw new DomainException("Criterion not found on this evaluation.");
        if (rawScore < 0 || rawScore > criterion.MaxScore)
        {
            throw new DomainException($"Score must be between 0 and {criterion.MaxScore}.");
        }
        if (criterion.IsFinancial && !IsTechnicallyQualifiedByEvaluator(evaluatorUserId, proposalId))
        {
            throw new DomainException("Cannot score a financial criterion: this proposal has not yet passed technical qualification for this evaluator.");
        }
        if (criterion.RequiresJustification
            && string.IsNullOrWhiteSpace(commentAr) && string.IsNullOrWhiteSpace(commentEn))
        {
            throw new DomainException("This criterion requires a justification comment.");
        }

        var existing = _scores.FirstOrDefault(s => s.EvaluatorUserId == evaluatorUserId && s.ProposalId == proposalId && s.CriterionId == criterionId);
        if (existing is not null)
        {
            existing.RawScore = rawScore;
            existing.CommentAr = commentAr;
            existing.CommentEn = commentEn;
            existing.ScoredAt = DateTimeOffset.UtcNow;
            return;
        }
        _scores.Add(new EvaluatorScore
        {
            Id = Guid.CreateVersion7(),
            EvaluationId = Id,
            EvaluatorUserId = evaluatorUserId,
            ProposalId = proposalId,
            CriterionId = criterionId,
            RawScore = rawScore,
            CommentAr = commentAr,
            CommentEn = commentEn,
            ScoredAt = DateTimeOffset.UtcNow,
        });
    }

    public void SubmitEvaluator(Guid evaluatorUserId, IReadOnlySet<Guid> proposalIds)
    {
        if (State != EvaluationState.InProgress)
        {
            throw new DomainException($"Cannot submit from state '{State}'; only 'InProgress' is valid.");
        }
        var assignment = ActiveAssignment(evaluatorUserId);
        if (assignment.SubmittedAt is not null)
        {
            throw new DomainException("This evaluator has already submitted.");
        }

        foreach (var proposalId in proposalIds)
        {
            if (!AllTechnicalCriteriaScored(evaluatorUserId, proposalId))
            {
                throw new DomainException("Cannot submit: all technical criteria must be scored for every proposal.");
            }
            if (IsTechnicallyQualifiedByEvaluator(evaluatorUserId, proposalId))
            {
                var financialCriteria = _criteria.Where(c => c.IsFinancial);
                var allFinancialScored = financialCriteria.All(c => _scores.Any(s => s.EvaluatorUserId == evaluatorUserId && s.ProposalId == proposalId && s.CriterionId == c.Id));
                if (!allFinancialScored)
                {
                    throw new DomainException("Cannot submit: all financial criteria must be scored for technically qualified proposals.");
                }
            }
        }

        assignment.SubmittedAt = DateTimeOffset.UtcNow;

        if (_assignments.Where(a => a.IsActive).All(a => a.SubmittedAt is not null))
        {
            State = EvaluationState.EvaluatorSubmitted;
        }
    }

    public void DeclareNoConflict(Guid evaluatorUserId)
    {
        var assignment = ActiveAssignment(evaluatorUserId);
        if (assignment.ConflictDeclaredAt is not null)
        {
            throw new DomainException("This evaluator has already made a conflict declaration.");
        }
        if (assignment.SubmittedAt is not null)
        {
            throw new DomainException("Cannot declare a conflict after submitting an evaluation.");
        }

        assignment.ConflictDeclaredAt = DateTimeOffset.UtcNow;
    }

    public void RecuseEvaluator(Guid evaluatorUserId, string reason)
    {
        if (State is not (EvaluationState.Assigned or EvaluationState.InProgress))
        {
            throw new DomainException($"Cannot recuse an evaluator from state '{State}'.");
        }
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("A recusal reason is required.");
        var assignment = ActiveAssignment(evaluatorUserId);
        if (assignment.SubmittedAt is not null)
        {
            throw new DomainException("Cannot recuse an evaluator who has already submitted.");
        }

        assignment.RecusedAt = DateTimeOffset.UtcNow;
        assignment.RecusalReason = reason;

        if (State == EvaluationState.InProgress && _assignments.Any(a => a.IsActive)
            && _assignments.Where(a => a.IsActive).All(a => a.SubmittedAt is not null))
        {
            State = EvaluationState.EvaluatorSubmitted;
        }
    }

    public sealed record BidTieBreakFacts(decimal? CommercialTotal, DateTimeOffset? SubmittedAt);

    public void Consolidate(IReadOnlyDictionary<Guid, BidTieBreakFacts>? bidFacts = null)
    {
        if (State != EvaluationState.EvaluatorSubmitted)
        {
            throw new DomainException($"Cannot consolidate from state '{State}'; only 'EvaluatorSubmitted' is valid.");
        }

        _results.Clear();
        var proposalIds = _scores.Select(s => s.ProposalId).Distinct().ToList();
        var provisional = new List<ConsolidatedResult>();

        foreach (var proposalId in proposalIds)
        {
            var technicalCriteria = _criteria.Where(c => !c.IsFinancial).ToList();
            var financialCriteria = _criteria.Where(c => c.IsFinancial).ToList();

            decimal AverageOrZero(Guid criterionId)
            {
                var relevant = _scores.Where(s => s.ProposalId == proposalId && s.CriterionId == criterionId).ToList();
                return relevant.Count == 0 ? 0m : relevant.Average(s => s.RawScore);
            }

            var qualified = technicalCriteria.All(c =>
            {
                var relevant = _scores.Where(s => s.ProposalId == proposalId && s.CriterionId == c.Id).ToList();
                if (relevant.Count == 0) return false;
                var avg = relevant.Average(s => s.RawScore);
                return c.Threshold is null || avg >= c.Threshold;
            });

            var technicalWeighted = technicalCriteria.Sum(c => c.MaxScore == 0 ? 0 : AverageOrZero(c.Id) / c.MaxScore * c.Weight);
            decimal? financialWeighted = qualified
                ? financialCriteria.Sum(c => c.MaxScore == 0 ? 0 : AverageOrZero(c.Id) / c.MaxScore * c.Weight)
                : null;

            provisional.Add(new ConsolidatedResult
            {
                Id = Guid.CreateVersion7(),
                EvaluationId = Id,
                ProposalId = proposalId,
                TechnicallyQualified = qualified,
                TechnicalWeightedScore = technicalWeighted,
                FinancialWeightedScore = financialWeighted,
                WeightedTotal = technicalWeighted + (financialWeighted ?? 0m),
            });
        }

        BidTieBreakFacts FactsFor(Guid proposalId) =>
            bidFacts is not null && bidFacts.TryGetValue(proposalId, out var facts) ? facts : new BidTieBreakFacts(null, null);

        var ranked = provisional
            .Where(r => r.TechnicallyQualified)
            .OrderByDescending(r => r.WeightedTotal)
            .ThenByDescending(r => r.TechnicalWeightedScore)
            .ThenBy(r => FactsFor(r.ProposalId).CommercialTotal ?? decimal.MaxValue)
            .ThenBy(r => FactsFor(r.ProposalId).SubmittedAt ?? DateTimeOffset.MaxValue)
            .ThenBy(r => r.ProposalId)
            .ToList();

        var rank = 1;
        foreach (var result in ranked)
        {
            result.Rank = rank++;
        }

        for (var i = 0; i < ranked.Count; i++)
        {
            for (var j = i + 1; j < ranked.Count; j++)
            {
                if (!IsFullTie(ranked[i], ranked[j], FactsFor)) continue;
                ranked[i].TieUnresolved = true;
                ranked[j].TieUnresolved = true;
            }
        }

        _results.AddRange(provisional);

        State = EvaluationState.Consolidated;
    }

    private static bool IsFullTie(
        ConsolidatedResult left, ConsolidatedResult right, Func<Guid, BidTieBreakFacts> factsFor)
    {
        if (left.WeightedTotal != right.WeightedTotal) return false;
        if (left.TechnicalWeightedScore != right.TechnicalWeightedScore) return false;

        var a = factsFor(left.ProposalId);
        var b = factsFor(right.ProposalId);
        return a.CommercialTotal == b.CommercialTotal && a.SubmittedAt == b.SubmittedAt;
    }

    public void ResolveTie(Guid proposalId, Guid resolvedByUserId, string reason)
    {
        if (State is not (EvaluationState.Consolidated or EvaluationState.Finalized))
        {
            throw new DomainException($"Cannot resolve a tie from state '{State}'; the evaluation must be consolidated.");
        }
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A reason is required to resolve a tie.");
        }

        var chosen = _results.FirstOrDefault(r => r.ProposalId == proposalId)
            ?? throw new DomainException("That proposal is not part of this evaluation's results.");
        if (!chosen.TieUnresolved)
        {
            throw new DomainException("That proposal is not part of an unresolved tie.");
        }

        var group = _results
            .Where(r => r.TieUnresolved
                && r.WeightedTotal == chosen.WeightedTotal
                && r.TechnicalWeightedScore == chosen.TechnicalWeightedScore)
            .OrderBy(r => r.Rank)
            .ToList();

        var ranks = group.Select(r => r.Rank).ToList();
        var ordered = new List<ConsolidatedResult> { chosen };
        ordered.AddRange(group.Where(r => r.ProposalId != proposalId));

        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Rank = ranks[i];
            ordered[i].TieUnresolved = false;
            ordered[i].TieResolvedByUserId = resolvedByUserId;
            ordered[i].TieResolutionReason = reason;
        }
    }

    public void FinalizeEvaluation()
    {
        if (State != EvaluationState.Consolidated)
        {
            throw new DomainException($"Cannot finalize from state '{State}'; only 'Consolidated' is valid.");
        }
        State = EvaluationState.Finalized;
    }

    public void ReopenForClarification(string reason)
    {
        if (State != EvaluationState.Consolidated)
        {
            throw new DomainException($"Cannot reopen from state '{State}'; only 'Consolidated' is valid.");
        }
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("A reason is required to reopen the evaluation.");

        _results.Clear();
        foreach (var assignment in _assignments.Where(a => a.IsActive))
        {
            assignment.SubmittedAt = null;
        }
        State = EvaluationState.InProgress;
    }
}
