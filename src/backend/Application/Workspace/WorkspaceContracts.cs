// The vocabulary for the guided workspace: one screen that gathers a tender's whole position in one
// place.
//
// It is a read built by combining the tender, its bids, its evaluation and its award. It stores nothing
// of its own, which is what the requirement asked for: orchestration over existing records rather than
// a new record.
//
// A stage is one step of the lifecycle tracker. Only the states this codebase can actually reach are
// listed. Three declared tender states remain unreachable, and a workspace that promised a stage
// nothing can ever enter would be actively misleading rather than merely incomplete.
//
// An action is one next step offered to the reader. Whether it is permitted reflects both the caller's
// own permission, computed on the server from their token and never taken from a client hint, and the
// domain's own precondition for that transition. The blocked reason explains whichever of the two is
// failing, or both, so the screen does not have to guess.
//
// A cancelled tender is reported as cancelled and nothing more. Which stage it was cancelled from is
// genuinely not recoverable once the state has moved, and inventing a position would be an inaccuracy
// rather than a helpful guess.

namespace MotsSupplierPortal.Application.Workspace;

public sealed record WorkspaceStageDto(string Key, bool IsCurrent, bool IsCompleted);

public sealed record WorkspaceActionDto(
    string Action, string LabelAr, string LabelEn, bool Permitted,
    string? BlockedReasonAr, string? BlockedReasonEn);

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
