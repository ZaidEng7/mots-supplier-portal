using MotsSupplierPortal.Domain.Proposals;

namespace MotsSupplierPortal.Application.Proposals;

/// <summary>
/// T-082 / SCR-430, SCR-431: the buyer's read of the bids received against their own RFQ.
///
/// <para><b>The gap this closes.</b> Both existing proposal reads are gated on
/// <c>proposal.create</c>, a SUPPLIER permission, so a buyer could not open a bid at all except
/// through the comparison matrix — which needs an opened evaluation. Between "submissions closed" and
/// "evaluation opened" the bids were unreadable by the people who have to act on them.</para>
///
/// <para><b>Three tiers, and they fail closed.</b> Nothing while the submission window is open;
/// identities and technical content from <c>SubmissionClosed</c>; commercial values only once the
/// evaluation is <c>Consolidated</c> or <c>Finalized</c> — the same seal
/// <c>BuyerVisibleProposal</c> already draws for documents (D-7/OQ-009), reused rather than
/// restated. Drafts are never listed in any tier: a draft is not a bid, and listing one tells the
/// buyer who is preparing to bid.</para>
/// </summary>
public enum BuyerProposalVisibility
{
    /// <summary>The window is still open. Nothing about individual bids is disclosed — knowing
    /// mid-tender who has and has not bid is leverage a buyer could hand to a favoured supplier, and
    /// no document permits it, so the default closes.</summary>
    Sealed,

    /// <summary>From SubmissionClosed: who bid, and what they said technically.</summary>
    Technical,

    /// <summary>From Consolidated/Finalized: the commercial figures as well.</summary>
    Commercial,
}

/// <param name="TotalValue">Null below <see cref="BuyerProposalVisibility.Commercial"/>. Absent
/// rather than zero: a zero would read as a free bid.</param>
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

/// <param name="Visibility">Stated on the wire so the screen can say WHY a figure is missing rather
/// than rendering a blank cell.</param>
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
    // Commercial tier only.
    decimal? TotalValue,
    string? CurrencyCode,
    string? PaymentTerms,
    string? IncotermCode);

public interface IListBuyerProposalsHandler
{
    /// <summary>Null when the RFQ is not this caller's — §9.2's 404, never a 403.</summary>
    Task<BuyerProposalListDto?> HandleAsync(string rfqReferenceCode, CancellationToken ct);
}

public interface IGetBuyerProposalHandler
{
    Task<BuyerProposalDetailDto?> HandleAsync(string rfqReferenceCode, Guid proposalId, CancellationToken ct);
}
