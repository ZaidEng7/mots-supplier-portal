namespace MotsSupplierPortal.Domain.Evaluation;

/// <summary>FEAT-11.2/FR-EVL-001, BUSINESS-PROCESSES.md §5.1: one row per assigned evaluator.
///
/// <para><b>Judgment call, flagged:</b> "scoped to that evaluator's assigned proposals" in the
/// task brief could mean a per-evaluator subset of proposals; nothing in BUSINESS-PROCESSES.md or
/// DOMAIN-MODEL.md specifies subset-assignment rules (which proposals go to which evaluator), so
/// this build assigns every evaluator to every Submitted proposal on the RFQ (the ordinary
/// committee-scoring pattern) rather than inventing an unspecified subsetting scheme. Every
/// EvaluatorScore row is still scoped to (EvaluatorUserId, ProposalId, CriterionId) individually,
/// so a future per-evaluator subset assignment would not require a schema change.</para></summary>
public sealed class EvaluationAssignment
{
    public Guid Id { get; init; }
    public Guid EvaluationId { get; init; }
    public Guid EvaluatorUserId { get; init; }
    public DateTimeOffset AssignedAt { get; init; }
    public DateTimeOffset? SubmittedAt { get; internal set; }
    public DateTimeOffset? RecusedAt { get; internal set; }
    public string? RecusalReason { get; internal set; }

    public bool IsActive => RecusedAt is null;
}
