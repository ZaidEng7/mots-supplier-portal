namespace MotsSupplierPortal.Domain.Evaluation;

/// <summary>Canonical machine (BUSINESS-PROCESSES.md §5): "NotStarted -&gt; Assigned -&gt;
/// InProgress -&gt; EvaluatorSubmitted -&gt; Consolidated -&gt; Finalized"; Consolidated may
/// re-open to InProgress with a mandatory reason.</summary>
public enum EvaluationState
{
    NotStarted,
    Assigned,
    InProgress,
    EvaluatorSubmitted,
    Consolidated,
    Finalized,
}
