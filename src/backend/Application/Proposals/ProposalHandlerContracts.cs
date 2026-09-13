using MotsSupplierPortal.Domain.Proposals;

namespace MotsSupplierPortal.Application.Proposals;

public interface IRequestProposalClarificationHandler
{
    Task<ProposalResult> HandleAsync(RequestProposalClarificationCommand command, CancellationToken ct);
}

public interface IReviseProposalHandler
{
    Task<ProposalResult> HandleAsync(ReviseProposalCommand command, CancellationToken ct);
}

/// <summary>§12.5: created at <c>POST /rfqs/{rfqCode}/proposals</c>, so this stays keyed on the
/// RFQ - the proposal has no code until it exists.</summary>
public interface IStartProposalHandler
{
    Task<ProposalResult> HandleAsync(string rfqReferenceCode, CancellationToken ct);
}

/// <summary>
/// Served at <c>GET /rfqs/{rfqCode}/proposals</c> - §3's named sub-collection
/// (<c>/rfqs/{rfqCode}/proposals</c>). Deliberately still RFQ-keyed: every other proposal route is
/// now addressed by <c>{proposalCode}</c>, and nothing in §12 documents how a returning supplier
/// discovers that code. This route is the answer, and it is the one §3 already names.
/// </summary>
public interface IGetProposalHandler
{
    Task<ProposalResult> HandleAsync(string rfqReferenceCode, CancellationToken ct);
}

/// <summary>
/// §12-A/C2: read a proposal by its own public code, the counterpart to §3's
/// <c>/proposals/{proposalCode}/items</c> and §12.5's <c>PATCH /proposals/{proposalCode}</c>.
///
/// <para>Added because without it the code-addressed READ did not exist, and the cross-org negative
/// for reading another supplier's proposal was passing on a 404 from an unrouted path rather than
/// from row-scoping - a vacuous test of exactly the kind this program exists to prevent. The
/// RFQ-scoped <see cref="IGetProposalHandler"/> stays as the discovery route.</para>
/// </summary>
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
    /// <summary>Null when the caller is not a supplier — §9.2's 404 rather than an empty list, which
    /// would assert that they have a supplier account with nothing in it.</summary>
    Task<IReadOnlyList<MyProposalListItemDto>?> HandleAsync(CancellationToken ct);
}
