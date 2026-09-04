using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Domain.Evaluation;

public sealed record CriterionSnapshotInput(
    string NameAr, string NameEn, CriterionDimension Dimension, decimal Weight, decimal MaxScore, decimal? Threshold,
    ScoringType ScoringType, bool RequiresJustification = false);

/// <summary>The scoring instance for one RFQ's Submitted proposals (docs/architecture/
/// DOMAIN-MODEL.md §5.7), using the RFQ's already-snapshotted EvaluationTemplate. Its own
/// aggregate root (schema "evaluation"), bound to RfqId - same "own bounded context, referenced
/// by id, not an Rfq child" shape as Proposal.
///
/// <para><b>The two-envelope technical-qualification gate (OQ-009) - the centerpiece of this
/// build, not in the original docs.</b> EPIC-09 built Proposal's financial content
/// (<c>ProposalItem</c>) as a genuinely separate table from its technical content. This aggregate
/// is what makes that separation matter operationally: <see cref="ScoreCriterion"/> refuses to
/// accept a score for a Commercial-dimension (financial) criterion on a given proposal until that
/// SAME evaluator has fully scored every technical-dimension criterion for that proposal and none
/// failed its threshold - see IsTechnicallyQualifiedByEvaluator. This is enforced per evaluator,
/// not globally, because scoring is blind/independent (OQ-005/BRULE-058): evaluator A's technical
/// judgment on a proposal cannot be influenced by whether evaluator B has opened its pricing yet,
/// and the gate must hold even mid-InProgress, long before Consolidate() ever runs. Consolidate()
/// re-derives TechnicallyQualified from the AVERAGED scores as the authoritative, final
/// determination (BRULE-064: "not shortlist-eligible ... regardless of total") - the per-evaluator
/// gate during scoring and the consolidated gate at the end are the same rule applied at two
/// different points, not two different rules.</para>
///
/// <para><b>Judgment call, flagged - quorum consolidation not built:</b> BRULE-066/
/// BUSINESS-PROCESSES.md §5.2 both tag "quorum policy" as [ASSUMPTION / REQUIRES BUSINESS
/// CONFIRMATION] with no number given. Consolidate() requires every actively-assigned evaluator to
/// have submitted - no partial-quorum path exists. A manager's actual tool for a non-responding
/// evaluator is <see cref="RecuseEvaluator"/> (BRULE-067's own recusal mechanism, reused rather
/// than inventing a second "exclude for non-response" action): removing them from the active set
/// changes what "every actively-assigned evaluator" means, without ever fabricating a quorum
/// fraction.</para></summary>
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

    /// <summary>BUSINESS-PROCESSES.md §5.1: "— -&gt; NotStarted: Evaluation created ... system (on
    /// RFQ UnderEvaluation) ... Instantiate criteria from EvaluationTemplate; snapshot weights".</summary>
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
            });
        }
        return evaluation;
    }

    /// <summary>FEAT-11.2/FR-EVL-001, BUSINESS-PROCESSES.md §5.1: NotStarted -&gt; Assigned on the
    /// first call; callable again later (Assigned/InProgress) to add more evaluators - the real
    /// tool for replacing a non-responding evaluator (recuse the old one, assign a new one)
    /// without inventing a separate "reassign" action.</summary>
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

    /// <summary>Assigned -&gt; InProgress on the first evaluator to open (BUSINESS-PROCESSES.md
    /// §5.1); a no-op state-wise for every evaluator after the first.</summary>
    public void OpenScoring(Guid evaluatorUserId)
    {
        ActiveAssignment(evaluatorUserId);
        if (State is not (EvaluationState.Assigned or EvaluationState.InProgress))
        {
            throw new DomainException($"Cannot open scoring from state '{State}'; only 'Assigned' or 'InProgress' is valid.");
        }
        State = EvaluationState.InProgress;
    }

    /// <summary>Technical-dimension criteria only need a score to exist; the gate itself is
    /// Threshold-based, matching BRULE-064's own threshold-gating language reused here as the
    /// qualification determinant.</summary>
    private bool AllTechnicalCriteriaScored(Guid evaluatorUserId, Guid proposalId) =>
        _criteria.Where(c => !c.IsFinancial)
            .All(c => _scores.Any(s => s.EvaluatorUserId == evaluatorUserId && s.ProposalId == proposalId && s.CriterionId == c.Id));

    /// <summary>The two-envelope gate's per-evaluator form - see Evaluation.cs's own class doc
    /// comment. False whenever a technical criterion is unscored OR scored below its threshold.</summary>
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

    /// <summary>FEAT-11.3/FR-EVL-003/004/005. <paramref name="proposalId"/> must be one of the
    /// RFQ's Submitted proposals - the handler passes the valid set (cross-aggregate, Proposal
    /// lives elsewhere) rather than this method trusting an arbitrary id.</summary>
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

        // T-021/BRULE-061: "Criteria requiring justification cannot be submitted without a comment."
        //
        // EITHER language satisfies it, not both. An evaluator writes their reasoning in the
        // language they think in, and demanding a translation from the person making the judgment
        // would either produce a machine-translated second copy or stop the score being recorded at
        // all. That is different from a SUPPLIER-facing field, where both languages are the product
        // (see the answer validation on RequirementAnswer): this comment is internal evidence for a
        // procurement file, read by the committee that wrote it and by an auditor after the fact.
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

    /// <summary>FEAT-11.5/FR-EVL-006, BRULE-062: "all assigned proposals fully scored" means every
    /// technical criterion for every proposal, PLUS every financial criterion for every proposal
    /// that passed technical qualification for this evaluator - a disqualified proposal legitimately
    /// never needs its financial criteria scored, so it is not held against submission.</summary>
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

    /// <summary>BRULE-067: recusal (or exclusion of a non-responding evaluator, FEAT-11.7/
    /// FR-EVL-011 - the same mechanism, see class doc comment on why no separate quorum path
    /// exists). Refused once already submitted - a submitted evaluator's scores are locked
    /// (BRULE-062), recusing them after the fact would silently discard a real, locked
    /// input.</summary>
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

    /// <summary>FEAT-11.6/FR-EVL-007, BRULE-063/064: per criterion, evaluator scores are averaged
    /// (default average, [ASSUMPTION / REQUIRES BUSINESS CONFIRMATION] - BRULE-063's own tag),
    /// multiplied by weight, summed. TechnicallyQualified is re-derived from the averaged scores
    /// here - the authoritative, final determination (per-evaluator qualification during scoring
    /// was necessarily provisional, based on one evaluator's view).</summary>
    public void Consolidate()
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

        var rank = 1;
        foreach (var result in provisional.Where(r => r.TechnicallyQualified).OrderByDescending(r => r.WeightedTotal))
        {
            result.Rank = rank++;
        }
        _results.AddRange(provisional);

        State = EvaluationState.Consolidated;
    }

    /// <summary>FEAT-11.6/FR-EVL-008, BUSINESS-PROCESSES.md §5.1: "Result reviewed; no unresolved
    /// clarification" - the "no unresolved clarification" half is EPIC-10's own Clarification
    /// aggregate, a different bounded context; not checked here (same cross-aggregate-or-not-built
    /// reasoning as every other guard this build could not verify against data it does not own).</summary>
    public void FinalizeEvaluation()
    {
        if (State != EvaluationState.Consolidated)
        {
            throw new DomainException($"Cannot finalize from state '{State}'; only 'Consolidated' is valid.");
        }
        State = EvaluationState.Finalized;
    }

    /// <summary>BUSINESS-PROCESSES.md §5.1: "Consolidated -&gt; InProgress: Re-open ... Reason
    /// mandatory". Unlocks every active assignment's submission (BUSINESS-PROCESSES.md's own
    /// "unlock affected assignments" is ambiguous about partial vs. full unlock - flagged, not
    /// silently resolved: this build unlocks all of them rather than inventing a
    /// which-ones-are-"affected" rule the docs never define).</summary>
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
