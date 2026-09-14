// What the bid reads and writes are called.
//
// Starting a bid is keyed on the tender rather than on the bid, because the bid has no code until it exists.
//
// Listing a supplier's bids for one tender is also keyed on the tender, and stays that way deliberately. Every
// other bid route is addressed by the bid's own code, and nothing in the written contract says how a returning
// supplier discovers that code. This read is the answer, and it is the one the contract already names.

namespace MotsSupplierPortal.Application.Proposals;

using MotsSupplierPortal.Domain.Proposals;

public interface IRequestProposalClarificationHandler
{
    Task<ProposalResult> HandleAsync(RequestProposalClarificationCommand command, CancellationToken ct);
}

public interface IReviseProposalHandler
{
    Task<ProposalResult> HandleAsync(ReviseProposalCommand command, CancellationToken ct);
}

public interface IStartProposalHandler
{
    Task<ProposalResult> HandleAsync(string rfqReferenceCode, CancellationToken ct);
}

public interface IGetProposalHandler
{
    Task<ProposalResult> HandleAsync(string rfqReferenceCode, CancellationToken ct);
}

public interface IGetProposalByCodeHandler
{
    Task<ProposalResult> HandleAsync(string proposalReferenceCode, CancellationToken ct);
}

public interface IManageProposalItemHandler
{
    Task<ProposalResult> SetAsync(SetItemPricingCommand command, CancellationToken ct);
    Task<ProposalResult> RemoveAsync(RemoveItemPricingCommand command, CancellationToken ct);
}

public interface ISetCommercialTermsHandler
{
    Task<ProposalResult> HandleAsync(SetCommercialTermsCommand command, CancellationToken ct);
}

public interface ISetNarrativeHandler
{
    Task<ProposalResult> HandleAsync(SetNarrativeCommand command, CancellationToken ct);
}

public interface IAnswerRequirementHandler
{
    Task<ProposalResult> HandleAsync(AnswerRequirementCommand command, CancellationToken ct);
}

public interface IManageProposalDocumentHandler
{
    Task<ProposalResult> AddAsync(AddProposalDocumentCommand command, CancellationToken ct);
    Task<ProposalResult> RemoveAsync(RemoveProposalDocumentCommand command, CancellationToken ct);
}

public interface ISubmitProposalHandler
{
    Task<ProposalResult> HandleAsync(SubmitProposalCommand command, CancellationToken ct);
}

public interface IWithdrawProposalHandler
{
    Task<ProposalResult> HandleAsync(WithdrawProposalCommand command, CancellationToken ct);
}

public interface IDeclineAwardOfferHandler
{
    Task<ProposalResult> HandleAsync(DeclineAwardOfferCommand command, CancellationToken ct);
}

public interface IListMyProposalsHandler
{
    Task<IReadOnlyList<MyProposalListItemDto>?> HandleAsync(CancellationToken ct);
}
