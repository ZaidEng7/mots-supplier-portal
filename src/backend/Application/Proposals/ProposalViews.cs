using MotsSupplierPortal.Domain.Proposals;

namespace MotsSupplierPortal.Application.Proposals;

/// <summary>Financial envelope DTO (OQ-009 two-envelope) - only ever included in ProposalDto, which
/// only the owning supplier's own handlers ever build (see ProposalDtoMapper.ToDto's own doc
/// comment).</summary>
public sealed record ProposalItemDto(
    Guid Id, Guid RfqItemId, decimal Quantity, decimal UnitPrice, decimal? Discount,
    decimal LineTotal, int? LeadTimeDays, string? NotesAr, string? NotesEn);

public sealed record ProposalDocumentDto(
    Guid Id, string OriginalFileName, string ContentType, string? Caption, DateTimeOffset UploadedAt,
    // T-028/D-7: the envelope this file declares itself to be in. Commercial when unstated.
    ProposalDocumentEnvelope Envelope);

public sealed record RequirementAnswerDto(Guid Id, Guid RequirementId, string AnswerAr, string AnswerEn);

/// <summary>The owning supplier's own full view - both envelopes together, since it is their own
/// proposal (FR-PRP-012 confidentiality is about OTHER parties, not the owner). No other DTO in
/// this file ever carries ProposalItemDto - that absence is the two-envelope seal for every party
/// but the owner, for this epic (EPIC-11 will add a technical-qualification-gated view for
/// evaluators; none exists yet, so no such view is defined here).</summary>
/// <summary>T-057: §12.5's submit response carries <c>totals { currency, grandTotal }</c> and its
/// PATCH response promises "recomputed totals". Derived from the line items on every read rather
/// than stored - a stored total is a second source of truth for a number the items already
/// determine, and the two drift the first time a line is edited outside the one path that
/// maintains it. Currency repeats the proposal's own so the object is self-describing, which is how
/// the document shows it.</summary>
public sealed record ProposalTotalsDto(string? Currency, decimal GrandTotal);

public sealed record ProposalDto(
    // R-9 rename pass. §12.5 names these proposalCode, rfqCode and currency.
    //
    // T-058 recorded "ProposalDto carries BOTH ReferenceCode and ProposalReferenceCode; one is
    // redundant". It was worse than redundant: the second field held the RFQ's code under a name
    // that said proposal, and every consumer reading it by name was reading a lie. The pair is now
    // proposalCode + rfqCode, which is both the fix and §12.5's own shape.
    string ProposalCode, string RfqCode, ProposalState State,
    string? Currency, string? PaymentTerms, string? IncotermCode, string? DeliveryTermsAr, string? DeliveryTermsEn,
    string? Warranty, DateOnly? ValidityStart, DateOnly? ValidityEnd,
    string? NarrativeAr, string? NarrativeEn,
    DateTimeOffset? SubmittedAt, DateTimeOffset? WithdrawnAt, string? WithdrawReason,
    // SCR-155. The aggregate has held §4.1's "Reason; specific questions" since T-051 and no
    // projection carried it, so a supplier could see the STATE ClarificationRequested and never the
    // question. That is not a screen the supplier can act on: revising in answer to an unstated
    // question is guesswork. Not a seal concern - this is the buyer's question addressed to this
    // bidder, and both are already parties to it.
    string? ClarificationReason, DateTimeOffset? ClarificationRequestedAt,
    // §4.1's "New revision n+1". Shown because a supplier on their third revision needs to know
    // that, and because Revised alone cannot say how many times.
    int RevisionNumber,
    IReadOnlyList<ProposalItemDto> Items, IReadOnlyList<ProposalDocumentDto> Documents, IReadOnlyList<RequirementAnswerDto> RequirementAnswers,
    // T-056: §12.5's create response shows createdAt and no DTO carried it. The aggregate has had
    // the column all along - this was a projection omission, not a missing fact.
    DateTimeOffset CreatedAt,
    ProposalTotalsDto Totals,
    /// <summary>
    /// §12.5's <c>validityDays</c>, DERIVED from the two dates rather than stored, and read-only.
    ///
    /// <para>The document models validity as one duration; this schema stores a start and an end,
    /// which carries strictly more information. Emitting the difference conforms the RESPONSE half
    /// without deciding anything. The request half is deliberately NOT accepted: turning a duration
    /// into an end date needs an anchor, and nothing in any document says whether the clock starts at
    /// creation, at submission, or at award - see DECISIONS-TAKEN.md D-22. Null when either date is
    /// absent, because a duration measured from nothing is not zero.</para>
    /// </summary>
    int? ValidityDays,
    // §8.1: the version this read saw, so the endpoint can emit it as an ETag and the caller can
    // send it back as If-Match. Carried on the DTO rather than fetched separately because the read
    // has already loaded the aggregate that knows it.
    uint RowVersion);

/// <summary>
/// SCR-150: the calling supplier's own proposals, across every RFQ.
///
/// <para><b>The gap.</b> A supplier could reach a proposal only through the RFQ that contains it —
/// there was no list scoped to them, so "what have I bid on" had no answer short of opening every
/// invitation in turn.</para>
/// </summary>
/// <param name="RfqTitleAr">Carried so the list reads as work rather than as codes. The RFQ is
/// already visible to this supplier (they were invited), so no new disclosure.</param>
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
