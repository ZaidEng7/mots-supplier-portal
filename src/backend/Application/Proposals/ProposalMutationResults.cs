using MotsSupplierPortal.Domain.Proposals;

namespace MotsSupplierPortal.Application.Proposals;

public abstract record ProposalResult
{
    public sealed record Success(ProposalDto Proposal) : ProposalResult;
    /// <summary>Covers "RFQ not found", "not invited", and "not Active" behind one outcome - same
    /// no-oracle reasoning as SupplierRfqResult.NotFoundOrNotInvited (EPIC-08).</summary>
    public sealed record NotFoundOrNotInvited : ProposalResult;
    /// <summary>
    /// T-065: carries the CURRENT STATE so the endpoint can answer §3's 409 with currentState and
    /// allowedNext, rather than the 400 every proposal endpoint used to return.
    ///
    /// <para>Nullable, because not every refusal is a transition refusal - some are shaped like
    /// validation ("a withdrawal reason is required") and have no meaningful allowed-next set. Those
    /// keep the 400 they always had; only a state-machine refusal becomes a 409, which is exactly
    /// what §3 governs.</para>
    /// </summary>
    public sealed record InvalidState(string Message, ProposalState? CurrentState = null) : ProposalResult;

    /// <summary>T-066: refused because the proposal is incomplete, not because of its state. §12.5
    /// answers this with 422 and a code naming what is missing.</summary>
    public sealed record Incomplete(string Error, string Message) : ProposalResult;
}
