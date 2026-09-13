// What can be asked of an evaluation: open it, assign a committee, score, submit, gather the scores, settle
// them, reopen them, and break a tie.
//
// Bids are named by their public code rather than an internal identifier, because internal identifiers stay
// out of payloads and a caller holding the comparison already has the code. The handler resolves it, which is
// also where an unknown code becomes the same not-found answer as a code belonging to another tender.
//
// Breaking a tie carries a mandatory reason, because a tie broken with no stated basis is exactly what the
// system refuses to do on its own.

namespace MotsSupplierPortal.Application.Evaluation;

using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Evaluation;

public sealed record ResolveEvaluationTieCommand(string RfqReferenceCode, string ProposalCode, string Reason);

public sealed record OpenEvaluationCommand(string RfqReferenceCode);

public sealed record AssignEvaluatorsCommand(string RfqReferenceCode, IReadOnlyList<Guid> EvaluatorUserIds);

public sealed record RecuseEvaluatorCommand(string RfqReferenceCode, Guid EvaluatorUserId, string Reason);

public sealed record ScoreCriterionCommand(string RfqReferenceCode, string ProposalCode, Guid CriterionId, decimal RawScore, string? CommentAr, string? CommentEn);

public sealed record SubmitEvaluatorCommand(string RfqReferenceCode);

public sealed record ConsolidateEvaluationCommand(string RfqReferenceCode);

public sealed record FinalizeEvaluationCommand(string RfqReferenceCode);

public sealed record ReopenEvaluationCommand(string RfqReferenceCode, string Reason);

public sealed record DeclareConflictCommand(string RfqReferenceCode, bool HasConflict, string? Reason);
