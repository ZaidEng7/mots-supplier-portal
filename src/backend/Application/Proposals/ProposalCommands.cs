using MotsSupplierPortal.Domain.Proposals;

namespace MotsSupplierPortal.Application.Proposals;

/// <summary>§4.1: UnderReview -&gt; ClarificationRequested. Reason is mandatory per the table's own
/// guard, "Reason; specific questions".</summary>
public sealed record RequestProposalClarificationCommand(string ProposalReferenceCode, string Reason);

/// <summary>§4.1: ClarificationRequested -&gt; Revised, the supplier's response.</summary>
public sealed record ReviseProposalCommand(string ProposalReferenceCode);

public sealed record SetItemPricingCommand(
    string ProposalReferenceCode, Guid RfqItemId, decimal Quantity, decimal UnitPrice, decimal? Discount, int? LeadTimeDays, string? NotesAr, string? NotesEn);

public sealed record RemoveItemPricingCommand(string ProposalReferenceCode, Guid RfqItemId);

public sealed record SetCommercialTermsCommand(
    string ProposalReferenceCode, string CurrencyCode, string? PaymentTerms, string? IncotermCode,
    string? DeliveryTermsAr, string? DeliveryTermsEn, string? Warranty, DateOnly? ValidityStart, DateOnly? ValidityEnd);

public sealed record SetNarrativeCommand(string ProposalReferenceCode, string? NarrativeAr, string? NarrativeEn);

public sealed record AnswerRequirementCommand(string ProposalReferenceCode, Guid RequirementId, string AnswerAr, string AnswerEn);

public sealed record AddProposalDocumentCommand(
    string ProposalReferenceCode, string StorageKey, string OriginalFileName, string ContentType, string? Caption,
    ProposalDocumentEnvelope Envelope = ProposalDocumentEnvelope.Commercial);

public sealed record RemoveProposalDocumentCommand(string ProposalReferenceCode, Guid DocumentId);

public sealed record SubmitProposalCommand(string ProposalReferenceCode);

public sealed record WithdrawProposalCommand(string ProposalReferenceCode, string Reason);

// T-064: the supplier's own decline of an award offer.
public sealed record DeclineAwardOfferCommand(string ProposalReferenceCode, string Reason);
