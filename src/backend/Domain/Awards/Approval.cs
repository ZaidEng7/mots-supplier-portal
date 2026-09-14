// One approval step on an award: who decided, what they decided, and when.
//
// Today every award has exactly one step, because the ministry confirmed a single approver
// whose decision is final. The list shape is kept anyway, so that a real multi-step
// approval chain later is a configuration change rather than a database migration. A
// tender's own approvals are shaped the same way, for the same reason.
//
// The setters are internal: only the Award itself records a decision, through Approve or
// Reject, which is where the rules live.

namespace MotsSupplierPortal.Domain.Awards;

public sealed class Approval
{
    public Guid Id { get; init; }
    public Guid AwardId { get; init; }
    public int StepNo { get; init; }
    public Guid? ApproverUserId { get; internal set; }
    public ApprovalDecision? Decision { get; internal set; }
    public string? Comment { get; internal set; }
    public DateTimeOffset? DecidedAt { get; internal set; }
}
