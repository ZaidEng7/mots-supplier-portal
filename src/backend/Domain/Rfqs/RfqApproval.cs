// One step of internal review on a tender: who was asked, who decided, and what they decided.
//
// Today there is a single approver per review pass, so exactly one pending step exists at a time. It is
// still modelled as an ordered, growing list of steps rather than one approver field on the tender, so
// that a later multi-level or amount-routed approval chain is an extension of this same shape rather
// than a database migration. An award's approvals already have exactly this shape.
//
// Returning a tender for edits resolves the pending step as rejected, carrying the reviewer's comments,
// rather than deleting it, so the history of every review pass is kept. The next submission for review
// creates a fresh pending step for the next pass.
//
// Nothing here encodes routing by amount or a quorum of approvers. That logic does not exist yet and
// must not be inferred from the shape.
//
// AssignedApproverUserId is the manager the step is waiting on, recorded when the step is created.
// ApproverUserId is who actually decided it, and is null while the step is pending.
//
// They are two fields on purpose. One field carrying both meanings would mean different things before
// and after a decision, and the case where that matters is real: a nominated approver who is
// unavailable and a colleague who decides in their place are two different people, and a record that
// keeps only the second cannot answer who was asked.
//
// AssignedApproverUserId is null when the pass named nobody, which is a recorded absence rather than a
// missing default.

namespace MotsSupplierPortal.Domain.Rfqs;

public enum RfqApprovalDecision
{
    Approved,
    Rejected,
}

public sealed class RfqApproval
{
    public Guid Id { get; init; }
    public Guid RfqId { get; init; }
    public int StepNo { get; init; }

    public Guid? AssignedApproverUserId { get; set; }

    public Guid? ApproverUserId { get; set; }
    public RfqApprovalDecision? Decision { get; set; }
    public string? Comment { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
}
