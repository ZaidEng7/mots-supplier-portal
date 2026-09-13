// What can be asked of a bid: start one, price its lines, set its terms and narrative, answer requirements,
// attach and remove documents, submit, withdraw, decline an award, and the two halves of a clarification.
//
// Requesting a clarification carries a mandatory reason, because the written rule asks for the reason and the
// specific questions.
//
// The supplier's response to a clarification carries nothing but the bid, because the answer is the edit they
// made to the bid itself.

namespace MotsSupplierPortal.Application.Proposals;

using MotsSupplierPortal.Domain.Proposals;

public sealed record RequestProposalClarificationCommand(string ProposalReferenceCode, string Reason);

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

public sealed record DeclineAwardOfferCommand(string ProposalReferenceCode, string Reason);
