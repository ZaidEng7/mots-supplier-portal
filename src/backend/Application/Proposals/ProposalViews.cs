// The shapes a bid is read through.
//
//
// THE TWO-ENVELOPE SEAL IS THE ABSENCE OF A FIELD
//
// The priced lines appear in exactly one shape: the owning supplier's own full view. No other shape in this
// file carries them at all, and that absence is the seal for every party except the owner.
//
// The owner sees both envelopes together, because the confidentiality rule is about other parties rather than
// about the company reading its own bid.
//
// There is no evaluator-facing view here with a technical-qualification gate on it. None exists, so none is
// defined, rather than defining one that nothing builds.
//
//
// TOTALS
//
// The submit response carries the currency and the grand total together, because a total without its currency
// is a number a supplier cannot check.

namespace MotsSupplierPortal.Application.Proposals;

using MotsSupplierPortal.Domain.Proposals;

public sealed record ProposalItemDto(
    Guid Id, Guid RfqItemId, decimal Quantity, decimal UnitPrice, decimal? Discount,
    decimal LineTotal, int? LeadTimeDays, string? NotesAr, string? NotesEn);

public sealed record ProposalDocumentDto(
    Guid Id, string OriginalFileName, string ContentType, string? Caption, DateTimeOffset UploadedAt,
    ProposalDocumentEnvelope Envelope);

public sealed record RequirementAnswerDto(Guid Id, Guid RequirementId, string AnswerAr, string AnswerEn);

public sealed record ProposalTotalsDto(string? Currency, decimal GrandTotal);

public sealed record ProposalDto(
    string ProposalCode, string RfqCode, ProposalState State,
    string? Currency, string? PaymentTerms, string? IncotermCode, string? DeliveryTermsAr, string? DeliveryTermsEn,
    string? Warranty, DateOnly? ValidityStart, DateOnly? ValidityEnd,
    string? NarrativeAr, string? NarrativeEn,
    DateTimeOffset? SubmittedAt, DateTimeOffset? WithdrawnAt, string? WithdrawReason,
    string? ClarificationReason, DateTimeOffset? ClarificationRequestedAt,
    int RevisionNumber,
    IReadOnlyList<ProposalItemDto> Items, IReadOnlyList<ProposalDocumentDto> Documents, IReadOnlyList<RequirementAnswerDto> RequirementAnswers,
    DateTimeOffset CreatedAt,
    ProposalTotalsDto Totals,
    int? ValidityDays,
    uint RowVersion);

public sealed record MyProposalListItemDto(
    string ProposalCode,
    string RfqCode,
    string RfqTitleAr,
    string RfqTitleEn,
    ProposalState State,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? SubmissionDeadline,
    string? CurrencyCode,
    decimal? TotalValue,
    int ItemCount);
