namespace MotsSupplierPortal.Domain.Rfqs;

public enum RfqApprovalDecision
{
    Approved,
    Rejected,
}

/// <summary>OQ-004 interim: RFQ internal-review/publish approval is a single configurable approver
/// today (docs/product/ASSUMPTIONS.md ASM-040), but modeled here as an ORDERED, GROWING array
/// (StepNo), not a scalar approver field, deliberately - so a later multi-level/threshold-routed
/// approval chain (docs/product/OPEN-QUESTIONS.md OQ-004) is a config/data extension onto this
/// same shape, not a schema migration. Same array-of-approval-steps shape as the already-designed
/// award.approval table (DATABASE-MODEL.md §2.6).
///
/// <para>This is genuinely interim, not final: with a single approver, exactly one pending
/// RfqApproval (StepNo=1, Decision=null) exists per review pass. A ReturnForEdits resolves that
/// pending row to Rejected (with the reviewer's comments) rather than deleting it, so the full
/// history of every review pass is preserved; the next SubmitForReview creates a fresh StepNo=1
/// pending row for the next pass. Nothing here encodes amount-threshold routing or multi-approver
/// quorum (BRULE-072/074) - that logic does not exist yet and must not be inferred from this
/// shape.</para></summary>
public sealed class RfqApproval
{
    public Guid Id { get; init; }
    public Guid RfqId { get; init; }
    public int StepNo { get; init; }
    public Guid? ApproverUserId { get; set; }
    public RfqApprovalDecision? Decision { get; set; }
    public string? Comment { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
}
