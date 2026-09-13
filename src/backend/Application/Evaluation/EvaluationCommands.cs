using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Evaluation;

namespace MotsSupplierPortal.Application.Evaluation;

/// <summary>A-1: a person breaks a tie the rules could not, and says why. Addressed by the proposal's
/// PUBLIC code, not its GUID - §3 keeps internal identifiers out of payloads, and a caller that has
/// the comparison has the code.</summary>
public sealed record ResolveEvaluationTieCommand(string RfqReferenceCode, string ProposalCode, string Reason);

public sealed record OpenEvaluationCommand(string RfqReferenceCode);

public sealed record AssignEvaluatorsCommand(string RfqReferenceCode, IReadOnlyList<Guid> EvaluatorUserIds);

public sealed record RecuseEvaluatorCommand(string RfqReferenceCode, Guid EvaluatorUserId, string Reason);

// T-068: addressed by the proposal's public code. Resolved to its GUID inside the handler, which is
// also where an unknown code becomes the same 404 as a code belonging to another RFQ.
public sealed record ScoreCriterionCommand(string RfqReferenceCode, string ProposalCode, Guid CriterionId, decimal RawScore, string? CommentAr, string? CommentEn);

public sealed record SubmitEvaluatorCommand(string RfqReferenceCode);

public sealed record ConsolidateEvaluationCommand(string RfqReferenceCode);

public sealed record FinalizeEvaluationCommand(string RfqReferenceCode);

public sealed record ReopenEvaluationCommand(string RfqReferenceCode, string Reason);

public sealed record DeclareConflictCommand(string RfqReferenceCode, bool HasConflict, string? Reason);
