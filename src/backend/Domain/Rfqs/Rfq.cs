using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Domain.Rfqs;

/// <summary>A buyer-authored Request for Quotation (docs/architecture/DOMAIN-MODEL.md §5.4);
/// buyer-internal until Published. The domain - not the API, not the UI - is the sole authority on
/// legal state transitions (BUSINESS-PROCESSES.md's own framing, same as Supplier.cs).
///
/// <para><b>Scope of this build (FEAT-07.1..07.10, this session):</b> Draft through
/// SubmissionOpen/SubmissionClosed, plus Cancel from any pre-Awarded state. UnderEvaluation onward
/// (Clarification/Shortlisting/Recommendation/AwardApproval/Awarded/Completed) are real
/// <see cref="RfqState"/> values per the canonical state machine, but NO domain method on this
/// aggregate transitions into them yet - that behavior belongs to EPIC-11/12/13/14
/// (FEAT-07.7, left as an explicit stub, not half-built).</para>
///
/// <para><b>FEAT-07.11 (ERP mapping fields) is also an explicit stub this session:</b>
/// <c>ExternalId</c>/<c>SyncStatus</c>/<c>LastSyncedAt</c> are deliberately NOT present on this
/// aggregate. Unlike Supplier/Organization, RFQ has no ERP push/pull integration wired up yet
/// (no Outbox handler consumes an RFQ event to sync it), so adding unused sync columns now would
/// be dead scaffolding; add them together with the actual ERP integration when it is built.</para>
/// </summary>
public sealed class Rfq
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
    public DateTimeOffset? PublishAt { get; private set; }
    public DateTimeOffset? SubmissionOpensAt { get; private set; }
    public DateTimeOffset? SubmissionClosesAt { get; private set; }
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
        DateTimeOffset? clarificationDeadlineAt, DateTimeOffset? evaluationTargetDate)
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
            PublishAt = publishAt,
            SubmissionOpensAt = submissionOpensAt,
            SubmissionClosesAt = submissionClosesAt,
            ClarificationDeadlineAt = clarificationDeadlineAt,
            EvaluationTargetDate = evaluationTargetDate,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>BRULE-033: submissionCloseAt must be strictly after submissionOpenAt (matches the
    /// DB CHECK `submission_closes_at >= submission_opens_at` - domain is stricter, using > rather
    /// than >=, since a zero-length window admits no submissions at all).
    ///
    /// <para><b>Ambiguity flagged, not resolved:</b> BRULE-033 also requires "a minimum open
    /// window" and gives "e.g. 3 business days" as an illustrative, not confirmed, example, tagged
    /// [ASSUMPTION]. No specific number is enforced here - inventing one would be silently
    /// resolving an open business question as if it were decided. Only close-after-open is
    /// enforced; a real minimum-window rule needs a business-confirmed number.</para></summary>
    private static void EnsureTimelineConsistent(DateTimeOffset? opensAt, DateTimeOffset? closesAt)
    {
        if (opensAt is not null && closesAt is not null && closesAt <= opensAt)
        {
            throw new DomainException("Submission close time must be strictly after the submission open time.");
        }
    }

    /// <summary>FEAT-07.10/FR-RFQ-012: full edit only in Draft. "Restricted in InternalReview" is
    /// implemented as "no content edits while under review" - the reviewer must ReturnForEdits
    /// (back to Draft) before the officer can change anything. "Locked after Published except
    /// addenda" - the addenda exception (FEAT-10.4) is EPIC-10 territory, not built this session;
    /// nothing here creates an addenda path, so Published+ is simply locked for now.</summary>
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

    public void RemoveItem(Guid itemId)
    {
        EnsureDraftEditable();
        var item = _items.FirstOrDefault(i => i.Id == itemId) ?? throw new DomainException("RFQ item not found.");
        _items.Remove(item);
        // Renumber so LineNo stays a dense 1..N sequence - the DB unique(rfq_id, line_no)
        // constraint (DATABASE-MODEL.md §2.3) would otherwise tolerate gaps fine, but a dense
        // sequence is what the authoring UI's line-number column expects to render.
        for (var i = 0; i < _items.Count; i++) _items[i].LineNo = i + 1;
    }

    public Requirement AddRequirement(string textAr, string textEn, bool isMandatory, string? documentTypeCode)
    {
        EnsureDraftEditable();
        if (string.IsNullOrWhiteSpace(textAr)) throw new DomainException("Requirement text (Arabic) is required.");
        if (string.IsNullOrWhiteSpace(textEn)) throw new DomainException("Requirement text (English) is required.");

        var requirement = new Requirement
        {
            Id = Guid.CreateVersion7(),
            RfqId = Id,
            TextAr = textAr,
            TextEn = textEn,
            IsMandatory = isMandatory,
            DocumentTypeCode = documentTypeCode,
        };
        _requirements.Add(requirement);
        return requirement;
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

    /// <summary>FR-RFQ-004/DOMAIN-MODEL.md §5.4: binds a version-snapshotted
    /// EvaluationTemplateRef{Id, snapshotVersion} - the caller passes the exact template Id/Version
    /// it resolved plus a pre-serialized JSON snapshot of that version's criteria (the RFQ never
    /// re-reads the live template after this point, even if that version is later forked).</summary>
    public void BindEvaluationTemplate(Guid evaluationTemplateId, int evaluationTemplateVersion, string snapshotJson)
    {
        EnsureDraftEditable();
        if (string.IsNullOrWhiteSpace(snapshotJson)) throw new DomainException("Evaluation template snapshot is required.");

        EvaluationTemplateId = evaluationTemplateId;
        EvaluationTemplateVersion = evaluationTemplateVersion;
        EvaluationTemplateSnapshotJson = snapshotJson;
    }

    /// <summary>FEAT-08.1/FR-INV-001/BRULE-032: invite a candidate supplier. Allowed from Draft
    /// through SubmissionOpen (not after SubmissionClosed) - this is what lets a candidate be
    /// identified before InternalReview (closing SubmitForReview's own gap below) while also
    /// covering FEAT-08.5's late-invite-while-open case with a single guard. "Only Active suppliers
    /// invitable" (BRULE-032) is NOT checked here - Supplier lifecycle lives on a different
    /// aggregate, so the caller (handler) verifies Active before invoking this, same cross-aggregate
    /// split already used for EvaluationTemplate.MarkReferenced.</summary>
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

    /// <summary>FEAT-08.6/FR-INV-006: called by the supplier-facing detail endpoint the first time
    /// an invited supplier views the RFQ. A no-op once the invitation has moved past Invited, so
    /// re-viewing never regresses a later status back to Viewed.</summary>
    public void MarkInvitationViewed(Guid supplierId)
    {
        var invitation = _invitations.FirstOrDefault(i => i.SupplierId == supplierId)
            ?? throw new DomainException("This supplier has no invitation to this RFQ.");
        if (invitation.Status != InvitationStatus.Invited) return;

        invitation.Status = InvitationStatus.Viewed;
        invitation.ViewedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>FEAT-08.4/FR-INV-004: supplier-initiated decline, optional reason. Refused once the
    /// invitation already carries a Submitted proposal - withdrawing a live proposal is EPIC-09's
    /// FEAT-09.6 (Withdraw), a different action from declining an invitation that was never acted
    /// on.</summary>
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

    /// <summary>FEAT-10.5/FR-CLR-005: the clarification window is Published or SubmissionOpen, and
    /// (when set) before ClarificationDeadlineAt - a refinement of the submission window
    /// (DOMAIN-MODEL.md §5.4's Timeline VO groups clarificationDeadline alongside submissionWindow),
    /// falling back to SubmissionClosesAt when no separate deadline was set. Judgment call, flagged:
    /// neither doc states this fallback explicitly; the alternative (no deadline = no window at all)
    /// would make ClarificationDeadlineAt's optionality meaningless, so "unset means it tracks the
    /// submission window" is the reading that keeps the field's own nullability coherent.</summary>
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

    /// <summary>FEAT-10.1/FR-CLR-001: invited-supplier-only is enforced by the caller (handler),
    /// same cross-aggregate split as InviteSupplier's own Active check - this method only enforces
    /// the window.</summary>
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

    /// <summary>FEAT-10.2/FR-CLR-002, OQ-008 interim: <paramref name="publish"/> defaults to false
    /// at the API layer (private-by-default) with publishing available as an explicit, separate
    /// choice - either here or later via PublishClarification. Refused once already answered: a
    /// buyer correcting an answer is a new clarification, not silently rewriting the audited
    /// one.</summary>
    public void AnswerClarification(Guid clarificationId, string answer, bool publish)
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
        clarification.Visibility = publish ? ClarificationVisibility.PublishedToAll : ClarificationVisibility.PrivateToAsker;
    }

    /// <summary>FEAT-10.2/FR-CLR-002: promotes an already-privately-answered clarification to
    /// PublishedToAll - the explicit publish action for a question answered privately at first.</summary>
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

    /// <summary>FEAT-10.4/FR-CLR-004/FR-RFQ-012: the first real use of "locked after Published
    /// except addenda" - allowed only once actually Published (an unpublished RFQ still uses normal
    /// Draft edits) and only while suppliers can still act on it (not after SubmissionClosed, since
    /// there is nothing left to inform them about in time to matter).</summary>
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

    /// <summary>Draft -> InternalReview (BUSINESS-PROCESSES.md §3.1: "Draft | InternalReview |
    /// Submit for review | procurement_officer / rfq.submit_review | >=1 RfqItem; deadlines set &amp;
    /// future; EvaluationTemplateRef bound; >=1 candidate supplier identified").
    ///
    /// <para><b>EPIC-08 gap closed:</b> "&gt;=1 candidate supplier identified" is now enforced
    /// against real Invitation rows (previously unenforced pending EPIC-08 - see git history on
    /// this method for the flagged gap this replaces).</para></summary>
    public void SubmitForReview()
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
        if (SubmissionOpensAt <= DateTimeOffset.UtcNow || SubmissionClosesAt <= DateTimeOffset.UtcNow)
        {
            throw new DomainException("Cannot submit for review: submission dates must be in the future.");
        }
        if (EvaluationTemplateId is null)
        {
            throw new DomainException("Cannot submit for review: an evaluation template must be bound.");
        }
        if (_invitations.Count == 0)
        {
            throw new DomainException("Cannot submit for review: at least one candidate supplier must be invited.");
        }

        _approvals.Add(new RfqApproval { Id = Guid.CreateVersion7(), RfqId = Id, StepNo = _approvals.Count + 1 });
        State = RfqState.InternalReview;
    }

    private RfqApproval CurrentPendingApproval() =>
        _approvals.LastOrDefault(a => a.Decision is null)
        ?? throw new DomainException("No pending approval found for this review pass.");

    /// <summary>InternalReview -> Draft ("return for edits", BUSINESS-PROCESSES.md §3.1: reason/
    /// comments provided by procurement_manager / rfq.review). Resolves the pending RfqApproval to
    /// Rejected with the reviewer's comments rather than deleting it, so the review history is
    /// preserved across passes (see RfqApproval's own doc comment).</summary>
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

    /// <summary>InternalReview -> Approved (BUSINESS-PROCESSES.md §3.1: procurement_manager /
    /// rfq.approve). OQ-004 interim: single approver resolves the one pending RfqApproval step -
    /// see RfqApproval's own doc comment for why this is modeled as an array even so.</summary>
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

    /// <summary>Approved -> Published (BUSINESS-PROCESSES.md §3.1: procurement_officer /
    /// rfq.publish; guard "Approved; invited suppliers are Active; submission open/close dates
    /// valid").
    ///
    /// <para><b>EPIC-08 gap closed, cross-aggregate:</b> "invited suppliers are Active" is now
    /// enforced, but not inside this method - Supplier lifecycle lives on a different aggregate, so
    /// PublishRfqHandler checks every invited supplier's LifecycleState before calling Publish() and
    /// refuses the whole operation (without ever calling this method) if any is not Active. Same
    /// split already used for EvaluationTemplate.MarkReferenced.</para></summary>
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
    }

    /// <summary>Published -> SubmissionOpen (BUSINESS-PROCESSES.md §3.1: system, "now &gt;=
    /// submissionOpenAt"). Driven by the scheduled RfqTimelineJob (FEAT-07.6/FR-PWF-004), not a
    /// user action - see that job for the actual time check; this method only enforces the state
    /// guard, the caller decides when to call it.</summary>
    public void OpenSubmissionWindow()
    {
        if (State != RfqState.Published)
        {
            throw new DomainException($"Cannot open the submission window from state '{State}'; only 'Published' is valid.");
        }

        State = RfqState.SubmissionOpen;
    }

    /// <summary>SubmissionOpen -> SubmissionClosed (BUSINESS-PROCESSES.md §3.1: system on deadline,
    /// or procurement_officer / rfq.close with reason for early close). <paramref name="reason"/>
    /// is required only for a manual early close - the scheduled deadline-driven close carries no
    /// reason since there is nothing to explain.</summary>
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

    /// <summary>SubmissionClosed -> UnderEvaluation (BUSINESS-PROCESSES.md §3.1:
    /// procurement_officer,procurement_manager / evaluation.open; guard "&gt;=1 Submitted proposal
    /// [ASSUMPTION] else re-tender/cancel; committee assignable"; "Create Evaluation; unlock
    /// scoring"). EPIC-11's real prerequisite: FEAT-07.7 left UnderEvaluation onward as an
    /// enum-only stub; this is the one transition into it this build actually needs, closing that
    /// specific piece of the stub rather than the whole thing (Clarification/Shortlisting/
    /// Recommendation/AwardApproval/Awarded/Completed remain unreachable, EPIC-13/14
    /// territory). The ">=1 Submitted proposal" guard is cross-aggregate (Proposal lives in a
    /// different aggregate) - OpenEvaluationHandler checks it before calling this, same split as
    /// every other cross-aggregate guard in this codebase.</summary>
    public void OpenEvaluation()
    {
        if (State != RfqState.SubmissionClosed)
        {
            throw new DomainException($"Cannot open evaluation from state '{State}'; only 'SubmissionClosed' is valid.");
        }

        State = RfqState.UnderEvaluation;
    }

    /// <summary>Cancel from any pre-Awarded state (BUSINESS-PROCESSES.md §3.1: procurement_manager
    /// / rfq.cancel, reason mandatory). Terminal - Cancelled has no outgoing transition.</summary>
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
