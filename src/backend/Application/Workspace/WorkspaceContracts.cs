namespace MotsSupplierPortal.Application.Workspace;

/// <summary>FEAT-13.1/FR-PWF-001: one stage in the guided lifecycle tracker. Only the states this
/// codebase's own domain methods can actually reach are listed - Clarification/Shortlisting/
/// Recommendation remain unreachable enum-only RfqState values (EPIC-13's own stub, see Rfq.cs's
/// doc comments), and a "guided" workspace that promised a stage nothing can ever enter would be
/// actively misleading rather than merely incomplete.</summary>
public sealed record WorkspaceStageDto(string Key, bool IsCurrent, bool IsCompleted);

/// <summary>One next-step affordance. <paramref name="Permitted"/> reflects BOTH the caller's own
/// permission claim (IScopeContext.HasPermission, computed server-side from the JWT, never trusted
/// from a client hint) AND the domain precondition for the transition; the blocker text explains
/// whichever one is failing (or both) so the UI need not guess.</summary>
public sealed record WorkspaceActionDto(
    string Action, string LabelAr, string LabelEn, bool Permitted,
    string? BlockedReasonAr, string? BlockedReasonEn);

/// <summary>A read-side aggregation over Rfq + Proposal + Evaluation + Award - no new persisted
/// state, exactly as BACKLOG.md's own "Domain: orchestration over RFQ + related aggregates (no new
/// aggregate)" note for this epic. Cancelled RFQs are represented by RfqState alone (IsCancelled)
/// rather than a synthetic guess at which stage the cancellation happened from - that information
/// genuinely is not recoverable from RfqState once it has moved to Cancelled, and fabricating a
/// stage position would be a real inaccuracy, not a helpful guess.</summary>
public sealed record WorkspaceDto(
    string RfqReferenceCode, string RfqState, bool IsCancelled,
    int SubmittedProposalCount,
    string? EvaluationState,
    string? AwardState,
    IReadOnlyList<WorkspaceStageDto> Stages,
    IReadOnlyList<WorkspaceActionDto> NextActions);

public interface IGetWorkspaceHandler
{
    Task<WorkspaceDto?> HandleAsync(string rfqReferenceCode, CancellationToken ct);
}
