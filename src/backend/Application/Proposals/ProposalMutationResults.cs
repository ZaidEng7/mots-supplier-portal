// What a bid write can answer.
//
// One outcome covers a tender that does not exist, a supplier who was not invited, and a supplier who cannot
// currently trade. Telling those apart would answer questions the caller is not entitled to ask.
//
//
// THE CURRENT STATE TRAVELS WITH A TRANSITION REFUSAL
//
// So the route can name the current state and what may legally follow, rather than answering a bare refusal
// as every bid route used to.
//
// It is optional, because not every refusal is about a transition. Some are shaped like validation, such as a
// missing withdrawal reason, and have no meaningful set of next states. Those keep the plain refusal they
// always had; only a lifecycle refusal becomes a conflict, which is what the rule governs.

namespace MotsSupplierPortal.Application.Proposals;

using MotsSupplierPortal.Domain.Proposals;

public abstract record ProposalResult
{
    public sealed record Success(ProposalDto Proposal) : ProposalResult;
    public sealed record NotFoundOrNotInvited : ProposalResult;
    public sealed record InvalidState(string Message, ProposalState? CurrentState = null) : ProposalResult;

    public sealed record Incomplete(string Error, string Message) : ProposalResult;
}
