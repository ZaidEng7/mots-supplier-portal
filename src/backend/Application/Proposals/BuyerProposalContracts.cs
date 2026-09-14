// The buyer's read of the bids received against their own tender.
//
//
// THE GAP THIS CLOSES
//
// Both existing bid reads were gated on a supplier permission, so a buyer could not open a bid at all except
// through the comparison matrix, which needs an opened evaluation.
//
// Between the window closing and the evaluation opening, the bids were unreadable by the people who have to
// act on them.
//
//
// THREE TIERS, AND THEY FAIL CLOSED
//
// Nothing at all while the submission window is still open.
//
// Identities and technical content once the window has closed.
//
// Commercial values only once the evaluation's scores have been gathered or settled.
//
// That is the same seal the document reads already draw, reused rather than restated, so there is one place
// where the tiers are decided.

namespace MotsSupplierPortal.Application.Proposals;

using MotsSupplierPortal.Domain.Proposals;

public enum BuyerProposalVisibility
{
    Sealed,

    Technical,

    Commercial,
}

public sealed record BuyerProposalListItemDto(
    Guid ProposalId,
    string ProposalCode,
    string SupplierNameAr,
    string SupplierNameEn,
    ProposalState State,
    DateTimeOffset? SubmittedAt,
    int ItemCount,
    int DocumentCount,
    decimal? TotalValue,
    string? CurrencyCode);

public sealed record BuyerProposalListDto(
    BuyerProposalVisibility Visibility,
    int SubmittedCount,
    IReadOnlyList<BuyerProposalListItemDto> Proposals);

public sealed record BuyerProposalItemDto(
    Guid RfqItemId, string TitleAr, string TitleEn, decimal Quantity,
    decimal? UnitPrice, decimal? LineTotal, int? LeadTimeDays, string? NotesAr, string? NotesEn);

public sealed record BuyerProposalAnswerDto(Guid RequirementId, string TextAr, string TextEn, string AnswerAr, string AnswerEn);

public sealed record BuyerProposalDetailDto(
    BuyerProposalVisibility Visibility,
    Guid ProposalId,
    string ProposalCode,
    string SupplierNameAr,
    string SupplierNameEn,
    ProposalState State,
    DateTimeOffset? SubmittedAt,
    string? NarrativeAr,
    string? NarrativeEn,
    IReadOnlyList<BuyerProposalAnswerDto> Answers,
    IReadOnlyList<BuyerProposalItemDto> Items,
    int DocumentCount,
    decimal? TotalValue,
    string? CurrencyCode,
    string? PaymentTerms,
    string? IncotermCode);

public interface IListBuyerProposalsHandler
{
    Task<BuyerProposalListDto?> HandleAsync(string rfqReferenceCode, CancellationToken ct);
}

public interface IGetBuyerProposalHandler
{
    Task<BuyerProposalDetailDto?> HandleAsync(string rfqReferenceCode, Guid proposalId, CancellationToken ct);
}
