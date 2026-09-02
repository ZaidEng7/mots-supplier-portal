using MotsSupplierPortal.Domain.Proposals;

namespace MotsSupplierPortal.Application.Proposals;

/// <summary>Financial envelope DTO (OQ-009 two-envelope) - only ever included in ProposalDto, which
/// only the owning supplier's own handlers ever build (see ProposalDtoMapper.ToDto's own doc
/// comment).</summary>
public sealed record ProposalItemDto(
    Guid Id, Guid RfqItemId, decimal Quantity, decimal UnitPrice, decimal? Discount,
    decimal LineTotal, int? LeadTimeDays, string? NotesAr, string? NotesEn);

public sealed record ProposalDocumentDto(Guid Id, string OriginalFileName, string ContentType, string? Caption, DateTimeOffset UploadedAt);

public sealed record RequirementAnswerDto(Guid Id, Guid RequirementId, string AnswerAr, string AnswerEn);

/// <summary>The owning supplier's own full view - both envelopes together, since it is their own
/// proposal (FR-PRP-012 confidentiality is about OTHER parties, not the owner). No other DTO in
/// this file ever carries ProposalItemDto - that absence is the two-envelope seal for every party
/// but the owner, for this epic (EPIC-11 will add a technical-qualification-gated view for
/// evaluators; none exists yet, so no such view is defined here).</summary>
public sealed record ProposalDto(
    string ReferenceCode, string ProposalReferenceCode, ProposalState State,
    string? CurrencyCode, string? PaymentTerms, string? IncotermCode, string? DeliveryTermsAr, string? DeliveryTermsEn,
    string? Warranty, DateOnly? ValidityStart, DateOnly? ValidityEnd,
    string? NarrativeAr, string? NarrativeEn,
    DateTimeOffset? SubmittedAt, DateTimeOffset? WithdrawnAt, string? WithdrawReason,
    IReadOnlyList<ProposalItemDto> Items, IReadOnlyList<ProposalDocumentDto> Documents, IReadOnlyList<RequirementAnswerDto> RequirementAnswers);

public sealed record SetItemPricingCommand(
    string ProposalReferenceCode, Guid RfqItemId, decimal Quantity, decimal UnitPrice, decimal? Discount, int? LeadTimeDays, string? NotesAr, string? NotesEn);

public sealed record RemoveItemPricingCommand(string ProposalReferenceCode, Guid RfqItemId);

public sealed record SetCommercialTermsCommand(
    string ProposalReferenceCode, string CurrencyCode, string? PaymentTerms, string? IncotermCode,
    string? DeliveryTermsAr, string? DeliveryTermsEn, string? Warranty, DateOnly? ValidityStart, DateOnly? ValidityEnd);

public sealed record SetNarrativeCommand(string ProposalReferenceCode, string? NarrativeAr, string? NarrativeEn);

public sealed record AnswerRequirementCommand(string ProposalReferenceCode, Guid RequirementId, string AnswerAr, string AnswerEn);

public sealed record AddProposalDocumentCommand(string ProposalReferenceCode, string StorageKey, string OriginalFileName, string ContentType, string? Caption);

public sealed record RemoveProposalDocumentCommand(string ProposalReferenceCode, Guid DocumentId);

public sealed record SubmitProposalCommand(string ProposalReferenceCode);

public sealed record WithdrawProposalCommand(string ProposalReferenceCode, string Reason);

public abstract record ProposalResult
{
    public sealed record Success(ProposalDto Proposal) : ProposalResult;
    /// <summary>Covers "RFQ not found", "not invited", and "not Active" behind one outcome - same
    /// no-oracle reasoning as SupplierRfqResult.NotFoundOrNotInvited (EPIC-08).</summary>
    public sealed record NotFoundOrNotInvited : ProposalResult;
    public sealed record InvalidState(string Message) : ProposalResult;
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
