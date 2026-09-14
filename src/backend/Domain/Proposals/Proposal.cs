// A supplier's bid against a published tender. It is its own record, not a part of the tender, and
// there is one per supplier per tender.
//
//
// THE TWO ENVELOPES, which is the central design decision here
//
// Evaluation is two-envelope: the technical content is opened and qualified first, and the pricing is
// opened only for suppliers who pass. An earlier default mixed technical and commercial scoring in one
// template; that default is stale and this build follows the newer instruction.
//
// Two envelopes cannot be bolted onto a shared row afterwards, so the split is structural rather than
// a display convention.
//
// The financial envelope is ProposalItem: unit price, quantity, discount, lead time. It is its own
// table and its own collection, which a query can leave out entirely rather than a flag on a row that
// somebody has to remember to filter. That is the actual price data, and the only thing "financial"
// means here.
//
// The technical envelope is everything else: the requirement answers, the documents, the narrative,
// and the commercial terms on this row.
//
// Classifying the commercial terms as technical is deliberate. None of payment terms, delivery terms,
// delivery term code or warranty is a price figure; they describe the conditions of the deal, the same
// category as compliance documents and requirement answers. That mirrors the real-world convention,
// where the technical and administrative envelope carries every non-price condition and the financial
// envelope carries only the price schedule. The written data model predates the two-envelope decision
// and does not draw this line, so the reasoning is stated here rather than assumed.
//
// CurrencyCode is metadata, which currency the prices are in, not a price itself. It is needed on the
// shared row so a currency label can render before the financial envelope is opened, so it is not
// withheld.
//
//
// THE LIFECYCLE
//
// Create starts a draft. The supplier must be active and hold an invitation to this tender, and there
// must be no existing bid; those are facts about other records, so the handler checks them, and a
// unique constraint on tender-and-supplier is the real guarantee. A second start returns the existing
// bid rather than failing.
//
// Everything is freely editable while the bid is a draft and refused afterwards, which is also why a
// draft is never visible to the buyer: nothing outside the owning supplier's own query path reads it
// before it is submitted.
//
// SetItemPricing and AnswerRequirement both replace rather than duplicate: pricing a line twice
// overwrites it, and answering a requirement twice overwrites the answer.
//
// Unit price must be greater than zero. The interface already refused zero and this guard permitted
// it, which made the rule's own home the laxer of the two. Nothing reaches this record except through
// that one endpoint today, but a second write path would have inherited the looser rule without
// anybody noticing.
//
// Submit is the real enforcement of the submission rules, not a formality: the window must be open,
// the current server time must be before the close, every required line must be priced, every
// mandatory requirement must be answered, and a validity end date must be set and not in the past.
// The window and the required-item and mandatory-requirement lists are facts about the tender, which
// the handler resolves and passes in. Using the server's own clock is what makes late submission
// impossible from a client whose clock is wrong or lying.
//
// Two written requirements are deliberately not enforced, and are flagged rather than quietly
// satisfied. "Mandatory documents attached" is not gated on at all, because nothing defines which
// documents are mandatory, unlike requirements, which carry their own flag. "Validity at least the
// tender's minimum" is not gated on either, because no tender field for a minimum validity exists
// anywhere; inventing a number would decide an undecided business rule in passing.
//
// Withdraw is the supplier's own retirement of a bid, from draft or submitted, and only while the
// tender's window is still open. A reason is required.
//
// OpenForReview moves a submitted bid into review, and it is the gateway the whole middle of the
// lifecycle hung on. Nothing used to assign that state, so nothing could reach clarification or the
// shortlist either: six of eleven states were unreachable and a bid went from draft to submitted
// straight to an outcome, skipping evaluation intake entirely. It is driven by the system rather than
// a person, so there is no permission check: the actor is the tender's own move into evaluation, and
// the caller is the handler performing it.
//
// RequestClarification asks the supplier a question, with a mandatory reason. RecordRevision is the
// supplier's answer, and ReturnToReview puts it back in front of the committee. That loop may repeat.
//
// RecordRevision increments the revision number and does not take a snapshot. The written rule asks
// for both, and for prior revisions to be immutable. Numbering is unambiguous and is implemented;
// snapshotting a bid's full prior content is a storage design nothing in this codebase has, and
// inventing one inside a state transition would decide the larger half of the requirement in passing.
// It also does not enforce which fields a revision may change: the written rule makes that a
// configurable policy whose default is undecided, so a guard here would have to invent the policy.
// Both are recorded rather than half-built.
//
// Shortlist marks a bid as still in contention. Two documents name different moments for this: the
// tender's own table puts it at recommendation time, and the bid's table puts it at consolidation.
// This follows the bid's table, because a bid's own transition table is the more specific authority
// and consolidation is where the threshold comparison actually happens. The conflict is reported here
// rather than resolved silently.
//
// OfferAward offers the contract to the winner, and it happens when the approver approves, not when
// an officer recommends. A recommendation is not yet a decision, and telling a bidder they have won
// before the approver has signed discloses an outcome that may still be reversed and cannot be
// un-told. Approval is the first point at which the offer is true.
//
// No acceptance window is enforced. An expiring offer would produce an outcome on its own, freeing
// the award for an alternate because a clock ran out, and that is the class of decision the system
// does not make for the ministry. The offer stays open until the supplier declines or the award is
// executed, and AwardOfferedAt is what makes a long-outstanding offer visible to an officer rather
// than invisible.
//
// DeclineAward requires a reason. The written table does not demand one, but every other
// supplier-initiated ending in this codebase does, and a declined award nobody can explain is the one
// an audit asks about first.
//
// Award accepts submitted, under review, shortlisted or offered. The canonical route is shortlist,
// then offer, then award, and it exists. The three earlier states stay valid because this codebase can
// also award directly out of the evaluation set for a tender that never went through shortlisting.
// Under review in particular is not optional: from the moment evaluation intake began working, the
// winner is in that state when the award is executed, and omitting it produced a server error on
// award.
//
// MarkNotSelected moves every other live bid on the tender, in the same save as the winner's award, so
// there is never a moment where some bids have been updated and others have not.
//
// Lapse is the submission window closing on a draft. Only a draft can lapse: a submitted bid missed
// nothing, and a finished one is already resolved. Both are refused rather than silently
// re-terminated, because a job that runs every few minutes must not be able to rewrite a decided
// outcome.
//
// CancelWithRfq is the tender being cancelled underneath a live bid. It works from any state that is
// not already final, because cancellation can arrive at any point before award. A finished bid is left
// alone: a withdrawn bid was withdrawn, and an awarded one belongs to a tender that could not have
// been cancelled.
//
// AllowedNextFrom is a promise to a caller about what it may attempt next, so it describes what the
// code actually accepts rather than only what the written table draws. Submitted and under review
// still list awarded and not-selected, because of the direct award path above.
//
//
// OTHER NOTES
//
// StateChangedAt is when the bid entered its current state. It is stamped by the persistence layer
// whenever the state actually changes, rather than by a line inside each transition above. It is null
// on rows that predate the column, because the moment those bids entered their current state was never
// recorded, and inventing one from a creation date would be the wrong answer rather than a missing one.
//
// The finance-system fields that other records carry are absent here on purpose: they arrive with the
// real integration and not before.
//
// Soft deletion is an open gap rather than a decision that it does not apply. The written data model
// lists bids as soft-delete-eligible, as evidence for audit and dispute, but no delete path of any
// kind exists in this build, and withdrawal is how a bid is actually retired. No soft-delete
// machinery exists anywhere in this codebase yet, not even for suppliers or tenders, which the same
// list also names. Building the columns here with nothing to ever set them would be dead scaffolding.

namespace MotsSupplierPortal.Domain.Proposals;

using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Suppliers;

public sealed class Proposal : IVersionedAggregate, IStateTimestamped
{
    private readonly List<ProposalItem> _items = [];
    private readonly List<ProposalDocument> _documents = [];
    private readonly List<RequirementAnswer> _requirementAnswers = [];

    public Guid Id { get; private init; }
    public string ReferenceCode { get; private init; } = null!;
    public Guid RfqId { get; private init; }
    public Guid SupplierId { get; private init; }
    public ProposalState State { get; private set; }

    public DateTimeOffset? StateChangedAt { get; private set; }

    public static string StatePropertyName => nameof(State);

    public string? CurrencyCode { get; private set; }
    public string? PaymentTerms { get; private set; }
    public string? IncotermCode { get; private set; }
    public string? DeliveryTermsAr { get; private set; }
    public string? DeliveryTermsEn { get; private set; }
    public string? Warranty { get; private set; }
    public DateOnly? ValidityStart { get; private set; }
    public DateOnly? ValidityEnd { get; private set; }

    public string? NarrativeAr { get; private set; }
    public string? NarrativeEn { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }
    public DateTimeOffset? WithdrawnAt { get; private set; }
    public string? WithdrawReason { get; private set; }

    public DateTimeOffset? AwardOfferedAt { get; private set; }

    public DateTimeOffset? DeclinedAt { get; private set; }

    public string? DeclineReason { get; private set; }

    public string? ClarificationReason { get; private set; }

    public DateTimeOffset? ClarificationRequestedAt { get; private set; }

    public int RevisionNumber { get; private set; } = 1;
    public DateTimeOffset CreatedAt { get; private init; }
    public uint RowVersion { get; private set; }

    public IReadOnlyList<ProposalItem> Items => _items;
    public IReadOnlyList<ProposalDocument> Documents => _documents;
    public IReadOnlyList<RequirementAnswer> RequirementAnswers => _requirementAnswers;

    private Proposal() { }

    public static Proposal Create(string referenceCode, Guid rfqId, Guid supplierId) => new()
    {
        Id = Guid.CreateVersion7(),
        ReferenceCode = referenceCode,
        RfqId = rfqId,
        SupplierId = supplierId,
        State = ProposalState.Draft,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private void EnsureDraftEditable()
    {
        if (State != ProposalState.Draft)
        {
            throw new DomainException($"Cannot edit this proposal from state '{State}'; only 'Draft' allows edits.");
        }
    }

    public void SetItemPricing(Guid rfqItemId, decimal quantity, decimal unitPrice, decimal? discount, int? leadTimeDays, string? notesAr, string? notesEn)
    {
        EnsureDraftEditable();
        if (quantity <= 0) throw new DomainException("Quantity must be positive.");
        if (unitPrice <= 0) throw new DomainException("Unit price must be greater than zero.");

        var existing = _items.FirstOrDefault(i => i.RfqItemId == rfqItemId);
        if (existing is not null)
        {
            existing.Quantity = quantity;
            existing.UnitPrice = unitPrice;
            existing.Discount = discount;
            existing.LeadTimeDays = leadTimeDays;
            existing.NotesAr = notesAr;
            existing.NotesEn = notesEn;
            return;
        }

        _items.Add(new ProposalItem
        {
            Id = Guid.CreateVersion7(),
            ProposalId = Id,
            RfqItemId = rfqItemId,
            Quantity = quantity,
            UnitPrice = unitPrice,
            Discount = discount,
            LeadTimeDays = leadTimeDays,
            NotesAr = notesAr,
            NotesEn = notesEn,
        });
    }

    public void RemoveItemPricing(Guid rfqItemId)
    {
        EnsureDraftEditable();
        var item = _items.FirstOrDefault(i => i.RfqItemId == rfqItemId)
            ?? throw new DomainException("No pricing recorded for this RFQ item.");
        _items.Remove(item);
    }

    public void SetCommercialTerms(
        string currencyCode, string? paymentTerms, string? incotermCode,
        string? deliveryTermsAr, string? deliveryTermsEn, string? warranty,
        DateOnly? validityStart, DateOnly? validityEnd)
    {
        EnsureDraftEditable();
        if (string.IsNullOrWhiteSpace(currencyCode)) throw new DomainException("Currency is required.");
        if (validityStart is not null && validityEnd is not null && validityEnd < validityStart)
        {
            throw new DomainException("Validity end date must not be before the validity start date.");
        }

        CurrencyCode = currencyCode;
        PaymentTerms = paymentTerms;
        IncotermCode = incotermCode;
        DeliveryTermsAr = deliveryTermsAr;
        DeliveryTermsEn = deliveryTermsEn;
        Warranty = warranty;
        ValidityStart = validityStart;
        ValidityEnd = validityEnd;
    }

    public void SetNarrative(string? narrativeAr, string? narrativeEn)
    {
        EnsureDraftEditable();
        NarrativeAr = narrativeAr;
        NarrativeEn = narrativeEn;
    }

    public void AnswerRequirement(Guid requirementId, string answerAr, string answerEn)
    {
        EnsureDraftEditable();
        if (string.IsNullOrWhiteSpace(answerAr)) throw new DomainException("Answer (Arabic) is required.");
        if (string.IsNullOrWhiteSpace(answerEn)) throw new DomainException("Answer (English) is required.");

        var existing = _requirementAnswers.FirstOrDefault(a => a.RequirementId == requirementId);
        if (existing is not null)
        {
            existing.AnswerAr = answerAr;
            existing.AnswerEn = answerEn;
            return;
        }

        _requirementAnswers.Add(new RequirementAnswer
        {
            Id = Guid.CreateVersion7(),
            ProposalId = Id,
            RequirementId = requirementId,
            AnswerAr = answerAr,
            AnswerEn = answerEn,
        });
    }

    public ProposalDocument AddDocument(
        string storageKey, string originalFileName, string contentType, string? caption,
        ProposalDocumentEnvelope envelope = ProposalDocumentEnvelope.Commercial)
    {
        EnsureDraftEditable();
        var document = new ProposalDocument
        {
            Envelope = envelope,
            Id = Guid.CreateVersion7(),
            ProposalId = Id,
            StorageKey = storageKey,
            OriginalFileName = originalFileName,
            ContentType = contentType,
            Caption = caption,
            UploadedAt = DateTimeOffset.UtcNow,
        };
        _documents.Add(document);
        return document;
    }

    public void RemoveDocument(Guid documentId)
    {
        EnsureDraftEditable();
        var document = _documents.FirstOrDefault(d => d.Id == documentId)
            ?? throw new DomainException("Document not found.");
        _documents.Remove(document);
    }

    public void Submit(bool rfqSubmissionOpen, DateTimeOffset submissionCloseAt, IReadOnlySet<Guid> requiredRfqItemIds, IReadOnlySet<Guid> mandatoryRequirementIds)
    {
        if (State != ProposalState.Draft)
        {
            throw new DomainException($"Cannot submit from state '{State}'; only 'Draft' is valid.");
        }
        if (!rfqSubmissionOpen)
        {
            throw new DomainException("Cannot submit: the RFQ is not currently accepting submissions.");
        }
        if (DateTimeOffset.UtcNow >= submissionCloseAt)
        {
            throw new DomainException("Cannot submit: the submission window has closed.");
        }
        var pricedItemIds = _items.Select(i => i.RfqItemId).ToHashSet();
        if (!requiredRfqItemIds.IsSubsetOf(pricedItemIds))
        {
            throw new ProposalIncompleteException(
                "proposal_items_required", "Cannot submit: all required RFQ items must be priced.");
        }
        var answeredRequirementIds = _requirementAnswers.Select(a => a.RequirementId).ToHashSet();
        if (!mandatoryRequirementIds.IsSubsetOf(answeredRequirementIds))
        {
            throw new ProposalIncompleteException(
                "proposal_requirements_required", "Cannot submit: all mandatory requirements must be answered.");
        }
        if (ValidityEnd is null)
        {
            throw new ProposalIncompleteException(
                "proposal_validity_required", "Cannot submit: a validity end date is required.");
        }
        if (ValidityEnd < DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date))
        {
            throw new ProposalIncompleteException(
                "proposal_validity_required", "Cannot submit: the validity end date must not be in the past.");
        }

        State = ProposalState.Submitted;
        SubmittedAt = DateTimeOffset.UtcNow;
    }

    public void Withdraw(string reason, bool rfqSubmissionOpen)
    {
        if (State is not (ProposalState.Draft or ProposalState.Submitted))
        {
            throw new DomainException($"Cannot withdraw from state '{State}'; only 'Draft' or 'Submitted' is valid.");
        }
        if (!rfqSubmissionOpen)
        {
            throw new DomainException("Cannot withdraw: the RFQ submission window is no longer open.");
        }
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("A withdrawal reason is required.");

        State = ProposalState.Withdrawn;
        WithdrawnAt = DateTimeOffset.UtcNow;
        WithdrawReason = reason;
    }

    public void OpenForReview()
    {
        if (State != ProposalState.Submitted)
        {
            throw new DomainException($"Cannot open for review from state '{State}'; only 'Submitted' is valid.");
        }

        State = ProposalState.UnderReview;
    }

    public void RequestClarification(string reason)
    {
        if (State != ProposalState.UnderReview)
        {
            throw new DomainException($"Cannot request clarification from state '{State}'; only 'UnderReview' is valid.");
        }
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A clarification reason is required.");
        }

        State = ProposalState.ClarificationRequested;
        ClarificationReason = reason;
        ClarificationRequestedAt = DateTimeOffset.UtcNow;
    }

    public void RecordRevision()
    {
        if (State != ProposalState.ClarificationRequested)
        {
            throw new DomainException($"Cannot revise from state '{State}'; only 'ClarificationRequested' is valid.");
        }

        State = ProposalState.Revised;
        RevisionNumber += 1;
    }

    public void ReturnToReview()
    {
        if (State != ProposalState.Revised)
        {
            throw new DomainException($"Cannot return to review from state '{State}'; only 'Revised' is valid.");
        }

        State = ProposalState.UnderReview;
    }

    public void Shortlist()
    {
        if (State != ProposalState.UnderReview)
        {
            throw new DomainException($"Cannot shortlist from state '{State}'; only 'UnderReview' is valid.");
        }

        State = ProposalState.Shortlisted;
    }

    public void OfferAward()
    {
        if (State != ProposalState.Shortlisted)
        {
            throw new DomainException($"Cannot offer an award from state '{State}'; only 'Shortlisted' is valid.");
        }

        State = ProposalState.AwardOffered;
        AwardOfferedAt = DateTimeOffset.UtcNow;
    }

    public void DeclineAward(string reason)
    {
        if (State != ProposalState.AwardOffered)
        {
            throw new DomainException($"Cannot decline from state '{State}'; only 'AwardOffered' is valid.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A reason is required to decline an award offer.");
        }

        State = ProposalState.Declined;
        DeclinedAt = DateTimeOffset.UtcNow;
        DeclineReason = reason;
    }

    public static IReadOnlyList<ProposalState> AllowedNextFrom(ProposalState state) => state switch
    {
        ProposalState.Draft => [ProposalState.Submitted, ProposalState.Withdrawn, ProposalState.Lapsed, ProposalState.Cancelled],

        ProposalState.Submitted =>
            [ProposalState.UnderReview, ProposalState.Withdrawn, ProposalState.Awarded, ProposalState.NotSelected, ProposalState.Cancelled],

        ProposalState.UnderReview =>
            [ProposalState.ClarificationRequested, ProposalState.Shortlisted, ProposalState.NotSelected, ProposalState.Awarded, ProposalState.Cancelled],

        ProposalState.ClarificationRequested => [ProposalState.Revised, ProposalState.Cancelled],
        ProposalState.Revised => [ProposalState.UnderReview, ProposalState.Cancelled],

        ProposalState.Shortlisted =>
            [ProposalState.AwardOffered, ProposalState.NotSelected, ProposalState.Awarded, ProposalState.Cancelled],

        ProposalState.AwardOffered => [ProposalState.Awarded, ProposalState.Declined],

        ProposalState.Awarded or ProposalState.NotSelected
            or ProposalState.Declined or ProposalState.Withdrawn
            or ProposalState.Lapsed or ProposalState.Cancelled => [],

        _ => [],
    };

    public void Lapse()
    {
        if (State != ProposalState.Draft)
        {
            throw new DomainException($"Cannot lapse a proposal in state '{State}'; only 'Draft' lapses when the window closes.");
        }

        State = ProposalState.Lapsed;
    }

    public void CancelWithRfq()
    {
        if (AllowedNextFrom(State).Count == 0)
        {
            throw new DomainException($"Cannot cancel a proposal in state '{State}'; it is already resolved.");
        }

        State = ProposalState.Cancelled;
    }

    public void Award()
    {
        if (State is not (ProposalState.Submitted or ProposalState.UnderReview
            or ProposalState.Shortlisted or ProposalState.AwardOffered))
        {
            throw new DomainException(
                $"Cannot award from state '{State}'; only 'Submitted', 'UnderReview', 'Shortlisted' or 'AwardOffered' is valid.");
        }
        State = ProposalState.Awarded;
    }

    public void MarkNotSelected()
    {
        if (State is not (ProposalState.Submitted or ProposalState.UnderReview or ProposalState.Shortlisted))
        {
            throw new DomainException(
                $"Cannot mark not-selected from state '{State}'; only 'Submitted', 'UnderReview' or 'Shortlisted' is valid.");
        }
        State = ProposalState.NotSelected;
    }
}
