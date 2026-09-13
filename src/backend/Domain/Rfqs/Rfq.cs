// A tender: what a buying body is asking to purchase, who is invited to bid, and when. It is
// buyer-internal until it is published.
//
// This record, not the API and not the interface, is the only authority on which state changes are
// legal. AllowedNextFrom is declared here beside the transitions rather than in an endpoint, because a
// machine whose legal moves live somewhere other than the record itself has two answers to the same
// question, and the one clients see is the one nobody runs.
//
// The finance-system fields that suppliers and organizations carry are deliberately absent. No sync of
// tenders is wired up, so unused columns now would be dead scaffolding; they arrive with the real
// integration.
//
//
// OWNERSHIP
//
// OwnerUserId is the officer who owns this tender, as a person rather than as a role.
//
// It exists because scoping a tender to its organization and stopping there meant every rule reading
// "notify the officer" reached the whole pool of officers and nobody was on record as responsible for
// a tender. That is an accountability gap, and it surfaced independently in three places: who gets
// notified, the buyer's "awaiting my action" tile, and the reassignment nobody could perform.
//
// The creator owns it at creation. That is not a placeholder to revisit: whoever wrote the tender is
// the one person unambiguously responsible for it the moment it exists, and reassignment handles every
// case after that.
//
// It is nullable and stays nullable. Every tender created before ownership existed has no owner and
// cannot be given one without guessing. The audit trail records who created each one, but a creator is
// not necessarily today's owner, and writing a guess into an ownership column is worse than an honest
// blank. An unowned tender falls back to the pool everywhere the owner is consulted, and the blank is
// a fact the buyer's list shows so somebody can claim it.
//
// Reassign refuses on completed and cancelled tenders, because those are finished, nothing is owed by
// anyone, and recording a responsibility that cannot be discharged is worse than recording none. An
// awarded tender is still reassignable, because post-award work exists and somebody has to own it.
//
// It also refuses when the new owner is already the owner. The point of the method is the audit row the
// caller writes beside it, and a row saying ownership changed from a person to the same person is a
// false entry in an append-only trail.
//
// Whether the nominee is actually an officer of this organization is a question about users, which is
// a different record, so the handler checks it before calling.
//
//
// TIMESTAMPS
//
// StateChangedAt is when the tender entered its current state, stamped by the persistence layer
// whenever the state actually changes rather than by a line in each transition below. It is null on
// rows that predate the column, because the moment those tenders entered their current state was never
// recorded, and inventing one from a creation date would be a wrong answer rather than a missing one.
//
// PublishAt and PublishedAt are different things and both are needed. PublishAt is a scheduled time,
// freely editable, an intention. PublishedAt is when the tender was actually published: set once, in
// Publish, and never again, because re-publishing is not a state this machine has and amending a live
// tender in place is forbidden anyway. A tender published immediately has no scheduled time at all.
//
// PublishedAt is null for every tender never published, which is why the buyer's list does not page on
// it: that list is mostly drafts, and paging on a nullable column drops them. The buyer's list pages on
// creation time instead.
//
//
// EDITING
//
// Content is fully editable only while the tender is a draft. "Restricted during internal review" is
// implemented as no content edits at all while under review: the reviewer has to return it for edits
// first. Once published it is locked, and an addendum is the only way to change anything.
//
// UpdateItem and UpdateRequirement correct a line or a requirement in place. They exist because the
// record had add and remove and nothing in between, so a mistyped quantity could only be fixed by
// deleting the line and typing it again, and deleting renumbers everything after it. An officer who
// meant to add one line and added three had no way to correct any of them.
//
// UpdateItem deliberately leaves the line number alone. This is the same line with better values, and
// a correction that reordered the tender would move every reference to "item 2" underneath the person
// reading it.
//
// Removing an item renumbers the rest so line numbers stay a dense run from one upward. The database
// would tolerate gaps, but the authoring screen's line-number column expects a dense sequence.
//
// A requirement's expected envelope is advisory guidance for the supplier, and it is refused on a
// requirement that asks for no document, because it would have nothing to attach to and would render
// as guidance about a file the supplier is never asked for.
//
// BindEvaluationTemplate stores the exact template version the caller resolved plus a frozen copy of
// that version's criteria. The tender never reads the live template again after this, even if that
// version is later forked.
//
//
// THE SUBMISSION WINDOW
//
// The close must be strictly after the open. The database merely requires that it is not before; this
// is stricter, because a zero-length window admits no bids at all.
//
// The written rule also asks for a minimum open window and offers three business days as an
// illustration rather than a decision. No number is enforced here, because inventing one would settle
// an open business question as though it had been decided.
//
// ChangeSubmissionDeadline handles both extending and shortening. One method, because the validity
// rules are identical and the only difference is who may call it, which is an access question the
// endpoint answers. It returns whether this was a shortening, so the caller can pick the right audit
// event and notification without working it out again.
//
// There is no cap on an extension. A cap is a fairness rule with an invented number in it, and a wrong
// cap blocks a legitimate extension during a real procurement with no way round it. The audit row and
// the notification to every invitee are what make an abusive extension visible instead.
//
// The new deadline must still be in the future and after the window opened. Those are not policy but
// coherence: a deadline in the past closes the tender on the timeline job's next run, so accepting one
// would let a shortening become an immediate close as a side effect, skipping the rules that closing
// has. And a close before the open leaves a window that never existed.
//
// A reason is mandatory, and this record enforces it rather than leaving it to the interface. A
// deadline moved with no stated basis is exactly what the requirement exists to prevent, and a second
// caller, a job or a future bulk tool, must not be able to bypass it by not going through the API.
//
// The reason is kept here so it is readable where the deadline is, on the tender, by the buyer and by
// every invited supplier. It is deliberately not in the notification, because the notification payload
// is restricted to identifiers and public codes, and free text is content by any reading. The
// notification says the deadline moved and points at the tender; the reason is waiting there.
//
// One consequence is worth stating: when no separate clarification deadline was set, the clarification
// window falls back to the submission deadline, so extending the deadline also reopens clarifications.
// That is the fallback behaving as designed, since a supplier given more time to bid should be able to
// ask about what they are bidding on, but nothing else says it out loud.
//
//
// INVITATIONS
//
// InviteSupplier is allowed from draft through to an open window, and not after the window closes. That
// single guard covers both identifying a candidate before review and inviting somebody late while the
// window is still open.
//
// Only active suppliers may be invited, and that is not checked here, because a supplier's lifecycle
// lives on a different record. The handler verifies it before calling. That split is used for every
// cross-record precondition in this codebase.
//
// MarkInvitationViewed is called the first time an invited supplier opens the tender, and does nothing
// once the invitation has moved past viewed, so re-reading never pushes a later status backwards.
//
// DeclineInvitation is the supplier's own refusal, with an optional reason. It is refused once the
// invitation carries a submitted bid, because withdrawing a live bid is a different action from
// declining an invitation nobody ever acted on.
//
//
// CLARIFICATIONS DURING THE WINDOW
//
// Questions may be asked while the tender is published or its window is open, and before the
// clarification deadline where one was set. When none was set, the window falls back to the submission
// deadline.
//
// That fallback is a judgement call, and neither document states it. The alternative, that no
// clarification deadline means no window at all, would make the field's optionality meaningless, so
// "unset means it tracks the submission window" is the reading that keeps the field coherent.
//
// Only invited suppliers may ask, and the handler enforces that. This record only enforces the window.
//
// Answering publishes the answer to every invitee. That reverses what was originally built. The code
// followed a recorded interim decision to keep answers private to the asker with publishing as a
// separate act, while the business rule says the opposite in as many words: material answers are
// broadcast to all invitees with the questioner anonymised. The conflict was resolved in favour of the
// business rule, because a private answer hands one bidder an advantage created by the buyer, and equal
// information to all bidders is the fundamental fairness principle in tendering.
//
// The asker is never identified in what other invitees receive: what is sent to a supplier carries no
// asker at all and works out on the server whether the reader is the one who asked. So the reason
// privacy was wanted, a bidder not revealing their thinking to competitors, survives. Only the
// information advantage goes.
//
// Answering again is refused. A buyer correcting an answer is a new clarification, not a silent
// rewrite of an audited one. And a question stays private until it is answered; nothing here publishes
// an unanswered thread.
//
// PublishClarification promotes an answer that was given privately at first.
//
//
// ADDENDA
//
// IssueAddendum is allowed only once the tender is actually published, since an unpublished tender uses
// ordinary draft edits, and only while suppliers can still act on it, since after the window closes
// there is nothing left to tell them in time to matter.
//
//
// THE TRANSITIONS
//
// SubmitForReview moves a draft into internal review. It requires at least one line, deadlines that are
// set and in the future, a bound evaluation template, and at least one invited supplier.
//
// The two date checks are named separately rather than as "dates", plural. The refusal a person
// actually meets is one date in the past and the other perfectly fine, and being told "dates" sends
// them to check the one that was never wrong. This was walked into: a window set to open a few minutes
// ahead had opened by the time the form was finished, and the message did not say which end had lapsed.
//
// The approver may be named, and the name is recorded on the pending step so that "notify the approver"
// resolves to a person rather than to everyone who holds the approval permission. It is optional, and a
// blank is not a defect: there is no approval-routing rule to fall back on, because routing by amount
// and multi-level chains are undecided, so choosing a manager here would invent the routing rather than
// record a decision. An un-nominated pass notifies the pool exactly as before, and whoever decides it
// is recorded as having decided it.
//
// ReturnForEdits sends it back to draft with the reviewer's comments. It resolves the pending approval
// step as rejected rather than deleting it, so the review history survives across passes.
//
// Approve resolves the pending step and moves the tender to approved.
//
// Publish makes it visible to the invited suppliers. Its written guard also requires that every invited
// supplier is active, and that is checked by the handler before this method is called, which refuses
// the whole operation if any is not.
//
// OpenSubmissions and the deadline-driven close are driven by the scheduled timeline job rather than by
// a person. Those methods enforce only the state guard; the caller decides when the time has come.
//
// Close also serves a manual early close by an officer, where a reason is required. The scheduled close
// at the deadline carries no reason, because there is nothing to explain.
//
// OpenEvaluation moves a closed tender into evaluation and creates the evaluation. Its written guard
// requires at least one submitted bid, which is a fact about bids, so the handler checks it.
//
// RequestEvaluationClarification is the evaluation-phase clarification, not the question-and-answer
// during the submission window. The two share a word and nothing else: one pauses the evaluation of a
// tender, the other is a question a supplier asks before bidding.
//
// ResolveEvaluationClarification returns it to evaluation. The written guard, a response received or
// the window elapsed, is not checkable here, because neither fact lives on this record. The state guard
// is what this method can enforce, and the officer's judgement is what the transition records. Named
// rather than pretended.
//
// BeginShortlisting requires the evaluation to be consolidated, which is a fact about the evaluation,
// so the caller checks it.
//
// RecordRecommendation records the named winner, and RouteAwardForApproval sends it to the approver.
//
// RouteAwardForApproval also accepts a tender still in evaluation, deliberately. Every tender that
// exists today reached approval directly from evaluation, because the three intermediate states were
// unreachable until they were built. There is no back-fill, and a guard admitting only the new path
// would strand those rows.
//
// ReturnToRecommendation is the tender's half of a supplier declining an award: the award frees up and
// the tender goes back to recommendation so an alternate can be chosen. This transition was listed as
// legal and unimplemented for a while, which meant the API promised a move it could not make. The award
// rejection path still does not use it, and that is recorded rather than changed, because rejection is
// a different flow with its own notifications.
//
// MarkAwarded is the tender's side of issuing an award; both happen in the same save.
//
// MarkCompleted waits until the finance system acknowledges the purchase order and its reference is
// stored, rather than firing when the award is issued.
//
// Cancel works from any state before awarded, with a mandatory reason, and is final.
//
// AllowedNextFrom lists what a caller may attempt next, which is what an illegal transition's refusal
// reports back. Evaluation still lists award approval directly, because of the rows described above.

namespace MotsSupplierPortal.Domain.Rfqs;

using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Domain.Proposals;

public sealed class Rfq : IVersionedAggregate, IStateTimestamped
{
    private readonly List<RfqItem> _items = [];
    private readonly List<Requirement> _requirements = [];
    private readonly List<RfqAttachment> _attachments = [];
    private readonly List<RfqApproval> _approvals = [];
    private readonly List<Invitation> _invitations = [];
    private readonly List<Clarification> _clarifications = [];
    private readonly List<Addendum> _addenda = [];

    public Guid Id { get; private init; }
    public string ReferenceCode { get; private init; } = null!;
    public Guid OrganizationId { get; private init; }
    public string TitleAr { get; private set; } = null!;
    public string TitleEn { get; private set; } = null!;
    public string? DescriptionAr { get; private set; }
    public string? DescriptionEn { get; private set; }
    public string CurrencyCode { get; private set; } = null!;
    public RfqState State { get; private set; }

    public DateTimeOffset? StateChangedAt { get; private set; }

    public static string StatePropertyName => nameof(State);

    public Guid? OwnerUserId { get; private set; }

    public DateTimeOffset? PublishAt { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }
    public DateTimeOffset? SubmissionOpensAt { get; private set; }
    public DateTimeOffset? SubmissionClosesAt { get; private set; }

    public string? SubmissionDeadlineChangeReason { get; private set; }

    public DateTimeOffset? SubmissionDeadlineChangedAt { get; private set; }
    public DateTimeOffset? ClarificationDeadlineAt { get; private set; }
    public DateTimeOffset? EvaluationTargetDate { get; private set; }
    public Guid? EvaluationTemplateId { get; private set; }
    public int? EvaluationTemplateVersion { get; private set; }
    public string? EvaluationTemplateSnapshotJson { get; private set; }
    public string? CancelReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }
    public uint RowVersion { get; private set; }

    public IReadOnlyList<RfqItem> Items => _items;
    public IReadOnlyList<Requirement> Requirements => _requirements;
    public IReadOnlyList<RfqAttachment> Attachments => _attachments;
    public IReadOnlyList<RfqApproval> Approvals => _approvals;
    public IReadOnlyList<Invitation> Invitations => _invitations;
    public IReadOnlyList<Clarification> Clarifications => _clarifications;
    public IReadOnlyList<Addendum> Addenda => _addenda;

    private Rfq() { }

    public static Rfq Create(
        string referenceCode, Guid organizationId, string titleAr, string titleEn,
        string? descriptionAr, string? descriptionEn, string currencyCode,
        DateTimeOffset? publishAt, DateTimeOffset? submissionOpensAt, DateTimeOffset? submissionClosesAt,
        DateTimeOffset? clarificationDeadlineAt, DateTimeOffset? evaluationTargetDate,
        Guid? ownerUserId = null)
    {
        if (string.IsNullOrWhiteSpace(titleAr)) throw new DomainException("RFQ title (Arabic) is required.");
        if (string.IsNullOrWhiteSpace(titleEn)) throw new DomainException("RFQ title (English) is required.");
        if (string.IsNullOrWhiteSpace(currencyCode)) throw new DomainException("RFQ currency is required.");
        EnsureTimelineConsistent(submissionOpensAt, submissionClosesAt);

        return new Rfq
        {
            Id = Guid.CreateVersion7(),
            ReferenceCode = referenceCode,
            OrganizationId = organizationId,
            TitleAr = titleAr,
            TitleEn = titleEn,
            DescriptionAr = descriptionAr,
            DescriptionEn = descriptionEn,
            CurrencyCode = currencyCode,
            State = RfqState.Draft,
            OwnerUserId = ownerUserId,
            PublishAt = publishAt,
            SubmissionOpensAt = submissionOpensAt,
            SubmissionClosesAt = submissionClosesAt,
            ClarificationDeadlineAt = clarificationDeadlineAt,
            EvaluationTargetDate = evaluationTargetDate,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Reassign(Guid newOwnerUserId)
    {
        if (State is RfqState.Completed or RfqState.Cancelled)
        {
            throw new DomainException($"Cannot reassign an RFQ in state '{State}'; it is closed and no action remains.");
        }
        if (OwnerUserId == newOwnerUserId)
        {
            throw new DomainException("This officer already owns the RFQ.");
        }

        OwnerUserId = newOwnerUserId;
    }

    private static void EnsureTimelineConsistent(DateTimeOffset? opensAt, DateTimeOffset? closesAt)
    {
        if (opensAt is not null && closesAt is not null && closesAt <= opensAt)
        {
            throw new DomainException("Submission close time must be strictly after the submission open time.");
        }
    }

    private void EnsureDraftEditable()
    {
        if (State != RfqState.Draft)
        {
            throw new DomainException($"Cannot edit RFQ content from state '{State}'; only 'Draft' allows edits.");
        }
    }

    public void UpdateBasics(string titleAr, string titleEn, string? descriptionAr, string? descriptionEn, string currencyCode,
        DateTimeOffset? publishAt, DateTimeOffset? submissionOpensAt, DateTimeOffset? submissionClosesAt,
        DateTimeOffset? clarificationDeadlineAt, DateTimeOffset? evaluationTargetDate)
    {
        EnsureDraftEditable();
        if (string.IsNullOrWhiteSpace(titleAr)) throw new DomainException("RFQ title (Arabic) is required.");
        if (string.IsNullOrWhiteSpace(titleEn)) throw new DomainException("RFQ title (English) is required.");
        if (string.IsNullOrWhiteSpace(currencyCode)) throw new DomainException("RFQ currency is required.");
        EnsureTimelineConsistent(submissionOpensAt, submissionClosesAt);

        TitleAr = titleAr;
        TitleEn = titleEn;
        DescriptionAr = descriptionAr;
        DescriptionEn = descriptionEn;
        CurrencyCode = currencyCode;
        PublishAt = publishAt;
        SubmissionOpensAt = submissionOpensAt;
        SubmissionClosesAt = submissionClosesAt;
        ClarificationDeadlineAt = clarificationDeadlineAt;
        EvaluationTargetDate = evaluationTargetDate;
    }

    public RfqItem AddItem(string titleAr, string titleEn, string? specificationAr, string? specificationEn,
        string categoryCode, decimal quantity, string unitOfMeasureCode, bool isUnitPrice, bool isOptional)
    {
        EnsureDraftEditable();
        if (string.IsNullOrWhiteSpace(titleAr)) throw new DomainException("Item title (Arabic) is required.");
        if (string.IsNullOrWhiteSpace(titleEn)) throw new DomainException("Item title (English) is required.");
        if (quantity <= 0) throw new DomainException("Item quantity must be positive.");

        var item = new RfqItem
        {
            Id = Guid.CreateVersion7(),
            RfqId = Id,
            LineNo = _items.Count + 1,
            TitleAr = titleAr,
            TitleEn = titleEn,
            SpecificationAr = specificationAr,
            SpecificationEn = specificationEn,
            CategoryCode = categoryCode,
            Quantity = quantity,
            UnitOfMeasureCode = unitOfMeasureCode,
            IsUnitPrice = isUnitPrice,
            IsOptional = isOptional,
        };
        _items.Add(item);
        return item;
    }

    public void UpdateItem(Guid itemId, string titleAr, string titleEn, string? specificationAr, string? specificationEn,
        string categoryCode, decimal quantity, string unitOfMeasureCode, bool isUnitPrice, bool isOptional)
    {
        EnsureDraftEditable();
        if (string.IsNullOrWhiteSpace(titleAr)) throw new DomainException("Item title (Arabic) is required.");
        if (string.IsNullOrWhiteSpace(titleEn)) throw new DomainException("Item title (English) is required.");
        if (quantity <= 0) throw new DomainException("Item quantity must be positive.");

        var item = _items.FirstOrDefault(i => i.Id == itemId) ?? throw new DomainException("RFQ item not found.");
        item.TitleAr = titleAr;
        item.TitleEn = titleEn;
        item.SpecificationAr = specificationAr;
        item.SpecificationEn = specificationEn;
        item.CategoryCode = categoryCode;
        item.Quantity = quantity;
        item.UnitOfMeasureCode = unitOfMeasureCode;
        item.IsUnitPrice = isUnitPrice;
        item.IsOptional = isOptional;
    }

    public void RemoveItem(Guid itemId)
    {
        EnsureDraftEditable();
        var item = _items.FirstOrDefault(i => i.Id == itemId) ?? throw new DomainException("RFQ item not found.");
        _items.Remove(item);
        for (var i = 0; i < _items.Count; i++) _items[i].LineNo = i + 1;
    }

    public Requirement AddRequirement(
        string textAr, string textEn, bool isMandatory, string? documentTypeCode,
        ProposalDocumentEnvelope? expectedEnvelope = null)
    {
        EnsureDraftEditable();
        if (string.IsNullOrWhiteSpace(textAr)) throw new DomainException("Requirement text (Arabic) is required.");
        if (string.IsNullOrWhiteSpace(textEn)) throw new DomainException("Requirement text (English) is required.");

        if (expectedEnvelope is not null && string.IsNullOrWhiteSpace(documentTypeCode))
        {
            throw new DomainException("An expected envelope only applies to a requirement that asks for a document.");
        }

        var requirement = new Requirement
        {
            Id = Guid.CreateVersion7(),
            RfqId = Id,
            TextAr = textAr,
            TextEn = textEn,
            IsMandatory = isMandatory,
            DocumentTypeCode = documentTypeCode,
            ExpectedEnvelope = expectedEnvelope,
        };
        _requirements.Add(requirement);
        return requirement;
    }

    public void UpdateRequirement(Guid requirementId, string textAr, string textEn, bool isMandatory,
        string? documentTypeCode, ProposalDocumentEnvelope? expectedEnvelope = null)
    {
        EnsureDraftEditable();
        if (string.IsNullOrWhiteSpace(textAr)) throw new DomainException("Requirement text (Arabic) is required.");
        if (string.IsNullOrWhiteSpace(textEn)) throw new DomainException("Requirement text (English) is required.");
        if (expectedEnvelope is not null && string.IsNullOrWhiteSpace(documentTypeCode))
        {
            throw new DomainException("An expected envelope only applies to a requirement that asks for a document.");
        }

        var requirement = _requirements.FirstOrDefault(r => r.Id == requirementId)
            ?? throw new DomainException("Requirement not found.");
        requirement.TextAr = textAr;
        requirement.TextEn = textEn;
        requirement.IsMandatory = isMandatory;
        requirement.DocumentTypeCode = documentTypeCode;
        requirement.ExpectedEnvelope = expectedEnvelope;
    }

    public void RemoveRequirement(Guid requirementId)
    {
        EnsureDraftEditable();
        var requirement = _requirements.FirstOrDefault(r => r.Id == requirementId)
            ?? throw new DomainException("Requirement not found.");
        _requirements.Remove(requirement);
    }

    public RfqAttachment AddAttachment(string storageKey, string originalFileName, string contentType, string? caption)
    {
        EnsureDraftEditable();
        var attachment = new RfqAttachment
        {
            Id = Guid.CreateVersion7(),
            RfqId = Id,
            StorageKey = storageKey,
            OriginalFileName = originalFileName,
            ContentType = contentType,
            Caption = caption,
            UploadedAt = DateTimeOffset.UtcNow,
        };
        _attachments.Add(attachment);
        return attachment;
    }

    public void RemoveAttachment(Guid attachmentId)
    {
        EnsureDraftEditable();
        var attachment = _attachments.FirstOrDefault(a => a.Id == attachmentId)
            ?? throw new DomainException("Attachment not found.");
        _attachments.Remove(attachment);
    }

    public void BindEvaluationTemplate(Guid evaluationTemplateId, int evaluationTemplateVersion, string snapshotJson)
    {
        EnsureDraftEditable();
        if (string.IsNullOrWhiteSpace(snapshotJson)) throw new DomainException("Evaluation template snapshot is required.");

        EvaluationTemplateId = evaluationTemplateId;
        EvaluationTemplateVersion = evaluationTemplateVersion;
        EvaluationTemplateSnapshotJson = snapshotJson;
    }

    public Invitation InviteSupplier(Guid supplierId)
    {
        if (State is RfqState.SubmissionClosed or RfqState.UnderEvaluation or RfqState.Clarification
            or RfqState.Shortlisting or RfqState.Recommendation or RfqState.AwardApproval
            or RfqState.Awarded or RfqState.Completed or RfqState.Cancelled)
        {
            throw new DomainException($"Cannot invite a supplier from state '{State}'; invitations are only allowed up to and including 'SubmissionOpen'.");
        }
        if (_invitations.Any(i => i.SupplierId == supplierId))
        {
            throw new DomainException("This supplier has already been invited.");
        }

        var invitation = new Invitation
        {
            Id = Guid.CreateVersion7(),
            RfqId = Id,
            SupplierId = supplierId,
            Status = InvitationStatus.Invited,
            InvitedAt = DateTimeOffset.UtcNow,
        };
        _invitations.Add(invitation);
        return invitation;
    }

    public void MarkInvitationViewed(Guid supplierId)
    {
        var invitation = _invitations.FirstOrDefault(i => i.SupplierId == supplierId)
            ?? throw new DomainException("This supplier has no invitation to this RFQ.");
        if (invitation.Status != InvitationStatus.Invited) return;

        invitation.Status = InvitationStatus.Viewed;
        invitation.ViewedAt = DateTimeOffset.UtcNow;
    }

    public void DeclineInvitation(Guid supplierId, string? reason)
    {
        var invitation = _invitations.FirstOrDefault(i => i.SupplierId == supplierId)
            ?? throw new DomainException("This supplier has no invitation to this RFQ.");
        if (invitation.Status == InvitationStatus.Submitted)
        {
            throw new DomainException("Cannot decline an invitation with an already-submitted proposal; withdraw the proposal instead.");
        }

        invitation.Status = InvitationStatus.Declined;
        invitation.DeclineReason = reason;
        invitation.RespondedAt = DateTimeOffset.UtcNow;
    }

    private void EnsureClarificationWindowOpen()
    {
        if (State is not (RfqState.Published or RfqState.SubmissionOpen))
        {
            throw new DomainException($"Cannot post a clarification from state '{State}'; the RFQ must be Published or SubmissionOpen.");
        }
        var deadline = ClarificationDeadlineAt ?? SubmissionClosesAt;
        if (deadline is not null && DateTimeOffset.UtcNow > deadline)
        {
            throw new DomainException("Cannot post a clarification: the clarification window has closed.");
        }
    }

    public Clarification PostClarificationQuestion(Guid supplierId, string question)
    {
        EnsureClarificationWindowOpen();
        if (string.IsNullOrWhiteSpace(question)) throw new DomainException("A question is required.");

        var clarification = new Clarification
        {
            Id = Guid.CreateVersion7(),
            RfqId = Id,
            AskedBySupplierId = supplierId,
            Question = question,
            AskedAt = DateTimeOffset.UtcNow,
        };
        _clarifications.Add(clarification);
        return clarification;
    }

    public void AnswerClarification(Guid clarificationId, string answer)
    {
        var clarification = _clarifications.FirstOrDefault(c => c.Id == clarificationId)
            ?? throw new DomainException("Clarification not found.");
        if (clarification.Answer is not null)
        {
            throw new DomainException("This clarification has already been answered.");
        }
        if (string.IsNullOrWhiteSpace(answer)) throw new DomainException("An answer is required.");

        clarification.Answer = answer;
        clarification.AnsweredAt = DateTimeOffset.UtcNow;
        clarification.Visibility = ClarificationVisibility.PublishedToAll;
    }

    public void PublishClarification(Guid clarificationId)
    {
        var clarification = _clarifications.FirstOrDefault(c => c.Id == clarificationId)
            ?? throw new DomainException("Clarification not found.");
        if (clarification.Answer is null)
        {
            throw new DomainException("Cannot publish a clarification that has not been answered yet.");
        }
        if (clarification.Visibility == ClarificationVisibility.PublishedToAll)
        {
            throw new DomainException("This clarification is already published.");
        }

        clarification.Visibility = ClarificationVisibility.PublishedToAll;
    }

    public Addendum IssueAddendum(string titleAr, string titleEn, string descriptionAr, string descriptionEn, Guid issuedByUserId)
    {
        if (State is not (RfqState.Published or RfqState.SubmissionOpen))
        {
            throw new DomainException($"Cannot issue an addendum from state '{State}'; the RFQ must be Published or SubmissionOpen.");
        }
        if (string.IsNullOrWhiteSpace(titleAr)) throw new DomainException("Addendum title (Arabic) is required.");
        if (string.IsNullOrWhiteSpace(titleEn)) throw new DomainException("Addendum title (English) is required.");
        if (string.IsNullOrWhiteSpace(descriptionAr)) throw new DomainException("Addendum description (Arabic) is required.");
        if (string.IsNullOrWhiteSpace(descriptionEn)) throw new DomainException("Addendum description (English) is required.");

        var addendum = new Addendum
        {
            Id = Guid.CreateVersion7(),
            RfqId = Id,
            TitleAr = titleAr,
            TitleEn = titleEn,
            DescriptionAr = descriptionAr,
            DescriptionEn = descriptionEn,
            IssuedAt = DateTimeOffset.UtcNow,
            IssuedByUserId = issuedByUserId,
        };
        _addenda.Add(addendum);
        return addendum;
    }

    public void SubmitForReview(Guid? assignedApproverUserId = null)
    {
        if (State != RfqState.Draft)
        {
            throw new DomainException($"Cannot submit for review from state '{State}'; only 'Draft' is valid.");
        }
        if (_items.Count == 0)
        {
            throw new DomainException("Cannot submit for review: at least one RFQ item is required.");
        }
        if (SubmissionOpensAt is null || SubmissionClosesAt is null)
        {
            throw new DomainException("Cannot submit for review: submission open/close dates must be set.");
        }
        if (SubmissionOpensAt <= DateTimeOffset.UtcNow)
        {
            throw new DomainException(
                "Cannot submit for review: the submission window has already opened. Set an opening date in the future.");
        }
        if (SubmissionClosesAt <= DateTimeOffset.UtcNow)
        {
            throw new DomainException(
                "Cannot submit for review: the submission window has already closed. Set a closing date in the future.");
        }
        if (EvaluationTemplateId is null)
        {
            throw new DomainException("Cannot submit for review: an evaluation template must be bound.");
        }
        if (_invitations.Count == 0)
        {
            throw new DomainException("Cannot submit for review: at least one candidate supplier must be invited.");
        }

        _approvals.Add(new RfqApproval
        {
            Id = Guid.CreateVersion7(), RfqId = Id, StepNo = _approvals.Count + 1,
            AssignedApproverUserId = assignedApproverUserId,
        });
        State = RfqState.InternalReview;
    }

    private RfqApproval CurrentPendingApproval() =>
        _approvals.LastOrDefault(a => a.Decision is null)
        ?? throw new DomainException("No pending approval found for this review pass.");

    public void ReturnForEdits(Guid approverUserId, string comments)
    {
        if (State != RfqState.InternalReview)
        {
            throw new DomainException($"Cannot return for edits from state '{State}'; only 'InternalReview' is valid.");
        }
        if (string.IsNullOrWhiteSpace(comments))
        {
            throw new DomainException("Comments are required when returning an RFQ for edits.");
        }

        var pending = CurrentPendingApproval();
        pending.ApproverUserId = approverUserId;
        pending.Decision = RfqApprovalDecision.Rejected;
        pending.Comment = comments;
        pending.DecidedAt = DateTimeOffset.UtcNow;

        State = RfqState.Draft;
    }

    public void Approve(Guid approverUserId)
    {
        if (State != RfqState.InternalReview)
        {
            throw new DomainException($"Cannot approve from state '{State}'; only 'InternalReview' is valid.");
        }

        var pending = CurrentPendingApproval();
        pending.ApproverUserId = approverUserId;
        pending.Decision = RfqApprovalDecision.Approved;
        pending.DecidedAt = DateTimeOffset.UtcNow;

        State = RfqState.Approved;
    }

    public void Publish()
    {
        if (State != RfqState.Approved)
        {
            throw new DomainException($"Cannot publish from state '{State}'; only 'Approved' is valid.");
        }
        if (SubmissionOpensAt is null || SubmissionClosesAt is null || SubmissionClosesAt <= SubmissionOpensAt)
        {
            throw new DomainException("Cannot publish: submission open/close dates are missing or invalid.");
        }

        State = RfqState.Published;
        PublishedAt = DateTimeOffset.UtcNow;
    }

    public void OpenSubmissionWindow()
    {
        if (State != RfqState.Published)
        {
            throw new DomainException($"Cannot open the submission window from state '{State}'; only 'Published' is valid.");
        }

        State = RfqState.SubmissionOpen;
    }

    public void CloseSubmissionWindow(string? reason, bool isEarlyClose)
    {
        if (State != RfqState.SubmissionOpen)
        {
            throw new DomainException($"Cannot close the submission window from state '{State}'; only 'SubmissionOpen' is valid.");
        }
        if (isEarlyClose && string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A reason is required to close the submission window early.");
        }

        State = RfqState.SubmissionClosed;
    }

    public void OpenEvaluation()
    {
        if (State != RfqState.SubmissionClosed)
        {
            throw new DomainException($"Cannot open evaluation from state '{State}'; only 'SubmissionClosed' is valid.");
        }

        State = RfqState.UnderEvaluation;
    }

    public static IReadOnlyList<RfqState> AllowedNextFrom(RfqState state) => state switch
    {
        RfqState.Draft => [RfqState.InternalReview, RfqState.Cancelled],
        RfqState.InternalReview => [RfqState.Draft, RfqState.Approved, RfqState.Cancelled],
        RfqState.Approved => [RfqState.Published, RfqState.Cancelled],
        RfqState.Published => [RfqState.SubmissionOpen, RfqState.Cancelled],
        RfqState.SubmissionOpen => [RfqState.SubmissionClosed, RfqState.Cancelled],
        RfqState.SubmissionClosed => [RfqState.UnderEvaluation, RfqState.Cancelled],

        RfqState.UnderEvaluation =>
            [RfqState.Clarification, RfqState.Shortlisting, RfqState.AwardApproval, RfqState.Cancelled],
        RfqState.Clarification => [RfqState.UnderEvaluation, RfqState.Cancelled],
        RfqState.Shortlisting => [RfqState.Recommendation, RfqState.Cancelled],
        RfqState.Recommendation => [RfqState.AwardApproval, RfqState.Cancelled],

        RfqState.AwardApproval => [RfqState.Awarded, RfqState.Recommendation, RfqState.Cancelled],
        RfqState.Awarded => [RfqState.Completed],

        RfqState.Completed or RfqState.Cancelled => [],
        _ => [],
    };

    public void RequestClarification(string reason)
    {
        if (State != RfqState.UnderEvaluation)
        {
            throw new DomainException($"Cannot request clarification from state '{State}'; only 'UnderEvaluation' is valid.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A reason is required to request clarification.");
        }

        State = RfqState.Clarification;
    }

    public void ResolveClarification()
    {
        if (State != RfqState.Clarification)
        {
            throw new DomainException($"Cannot resolve clarification from state '{State}'; only 'Clarification' is valid.");
        }

        State = RfqState.UnderEvaluation;
    }

    public void BeginShortlisting()
    {
        if (State != RfqState.UnderEvaluation)
        {
            throw new DomainException($"Cannot begin shortlisting from state '{State}'; only 'UnderEvaluation' is valid.");
        }

        State = RfqState.Shortlisting;
    }

    public void RecordRecommendation()
    {
        if (State != RfqState.Shortlisting)
        {
            throw new DomainException($"Cannot record a recommendation from state '{State}'; only 'Shortlisting' is valid.");
        }

        State = RfqState.Recommendation;
    }

    public void EnterAwardApproval()
    {
        if (State is not (RfqState.Recommendation or RfqState.UnderEvaluation))
        {
            throw new DomainException(
                $"Cannot enter award approval from state '{State}'; only 'Recommendation' or 'UnderEvaluation' is valid.");
        }
        State = RfqState.AwardApproval;
    }

    public void ReturnToRecommendation()
    {
        if (State != RfqState.AwardApproval)
        {
            throw new DomainException(
                $"Cannot return to recommendation from state '{State}'; only 'AwardApproval' is valid.");
        }

        State = RfqState.Recommendation;
    }

    public bool ChangeSubmissionDeadline(DateTimeOffset newCloseAt, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A reason is required to change the submission deadline.");
        }

        if (State is not (RfqState.Published or RfqState.SubmissionOpen))
        {
            throw new DomainException(
                $"Cannot change the submission deadline from state '{State}'; only 'Published' or 'SubmissionOpen' is valid.");
        }

        if (SubmissionClosesAt is null)
        {
            throw new DomainException("Cannot change the submission deadline: this RFQ has none set.");
        }

        if (newCloseAt <= DateTimeOffset.UtcNow)
        {
            throw new DomainException("The new submission deadline must be in the future.");
        }

        if (SubmissionOpensAt is not null && newCloseAt <= SubmissionOpensAt)
        {
            throw new DomainException("The new submission deadline must be after the submission window opens.");
        }

        if (newCloseAt == SubmissionClosesAt)
        {
            throw new DomainException("The new submission deadline is the same as the current one.");
        }

        var isShortening = newCloseAt < SubmissionClosesAt;
        SubmissionClosesAt = newCloseAt;

        SubmissionDeadlineChangeReason = reason;
        SubmissionDeadlineChangedAt = DateTimeOffset.UtcNow;
        return isShortening;
    }

    public void MarkAwarded()
    {
        if (State != RfqState.AwardApproval)
        {
            throw new DomainException($"Cannot mark awarded from state '{State}'; only 'AwardApproval' is valid.");
        }
        State = RfqState.Awarded;
    }

    public void Complete()
    {
        if (State != RfqState.Awarded)
        {
            throw new DomainException($"Cannot complete from state '{State}'; only 'Awarded' is valid.");
        }
        State = RfqState.Completed;
    }

    public void Cancel(string reason)
    {
        if (State is RfqState.Awarded or RfqState.Completed or RfqState.Cancelled)
        {
            throw new DomainException($"Cannot cancel an RFQ in state '{State}'; cancellation is only allowed pre-Awarded.");
        }
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A cancellation reason is required.");
        }

        CancelReason = reason;
        State = RfqState.Cancelled;
    }
}
