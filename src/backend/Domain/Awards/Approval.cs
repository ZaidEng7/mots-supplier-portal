namespace MotsSupplierPortal.Domain.Awards;

/// <summary>FEAT-14.2/14.3/FR-AWD-002/003, DATABASE-MODEL-shaped exactly like Rfq.RfqApproval
/// (Api's own established "an ordered, growing array of review passes" pattern, StepNo always 1
/// today) - OQ-004 resolved single-approver/final, so this build never sets StepNo above 1, but the
/// array shape means a real multi-step chain later is a config/data extension, not a schema
/// migration, exactly like RfqApproval's own reasoning.</summary>
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
