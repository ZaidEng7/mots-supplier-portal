using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Domain.Proposals;

/// <summary>A supplier's bid against a published RFQ (docs/architecture/DOMAIN-MODEL.md §5.5) - its
/// own aggregate root, not a child of Rfq (DOMAIN-MODEL.md: "Aggregate: Proposal.ProposalState"),
/// one per (SupplierId, RfqId).
///
/// <para><b>Two-envelope separation (OQ-009 resolution), the central design decision of this
/// build:</b> the project has resolved OQ-009 in favour of two-envelope evaluation (technical
/// opened and qualified first; financial opened only for suppliers who pass technical
/// qualification) - the opposite of ASM-052/OPEN-QUESTIONS.md's still-recorded default ("single
/// weighted template mixing technical + commercial"). That default is now stale; this build follows
/// the newer instruction. Two-envelope cannot be retrofitted onto a shared-row schema (OQ-009's own
/// rationale), so the split is structural, not a display convention:</para>
///
/// <para><b>Financial envelope</b> - <see cref="ProposalItem"/> (unit price, quantity, discount,
/// lead time): its own table (proposal.proposal_item), a genuinely separate EF collection that a
/// query can omit entirely (never `.Include(p => p.Items)`), not a flag on a shared row. This is
/// the actual price data - the only thing "financial" means here.</para>
///
/// <para><b>Technical envelope</b> - everything else: <see cref="RequirementAnswer"/>[],
/// <see cref="ProposalDocument"/>[], and CommercialTerms' own fields (CurrencyCode, PaymentTerms,
/// IncotermCode, DeliveryTerms, Warranty, Validity). CommercialTerms' non-price fields are
/// deliberately classified technical, not financial: none of payment terms, delivery/lead-time
/// terms, incoterm, or warranty is a price figure - they describe the deal's conditions, the same
/// category as compliance documents and requirement answers. This mirrors the real-world two-envelope
/// convention (e.g. World Bank/MDB procurement: the technical/administrative envelope carries every
/// non-price condition, the financial envelope carries only the price schedule) - DOMAIN-MODEL.md
/// predates the two-envelope decision and does not call this split out, so this reasoning is stated
/// here rather than silently assumed.</para>
///
/// <para>CurrencyCode is metadata (which currency the prices are in), not a price value itself, and
/// is needed on the shared row so a display-currency label can render before financial is opened
/// (FR-PRP-006) - it is not withheld.</para>
///
/// <para><b>FEAT-09.7/FEAT-09.9 explicit stubs:</b> no domain method here transitions past Submitted
/// into UnderReview/Shortlisted/etc (see ProposalState.cs's own doc comment), and ExternalId/
/// SyncStatus/LastSyncedAt (FEAT-09.9, FR-PRP-013) are not present - same "add with the real
/// integration, not before" reasoning as Rfq.cs's own FEAT-07.11 stub.</para>
///
/// <para><b>Soft-delete gap, flagged not silently skipped:</b> DATABASE-MODEL.md §9 lists
/// `proposal.proposal` as soft-delete-eligible ("bid evidence for audit/dispute"). No delete
/// endpoint of any kind exists in this build - Withdraw is Proposal's actual retirement mechanism,
/// same reasoning EPIC-07 already applied to Rfq. No soft-delete infrastructure (IsDeleted, a query
/// filter, anything) exists anywhere in this codebase yet, not even for Supplier or Rfq, which the
/// same table also lists. Building bespoke soft-delete columns here with no delete path to ever set
/// them would be dead scaffolding; this is an open gap for whichever future work adds a real delete
/// affordance (or a codebase-wide soft-delete pass), not a decision that soft-delete doesn't
/// apply.</para></summary>
public sealed class Proposal : IVersionedAggregate
{
    private readonly List<ProposalItem> _items = [];
    private readonly List<ProposalDocument> _documents = [];
    private readonly List<RequirementAnswer> _requirementAnswers = [];

    public Guid Id { get; private init; }
    public string ReferenceCode { get; private init; } = null!;
    public Guid RfqId { get; private init; }
    public Guid SupplierId { get; private init; }
    public ProposalState State { get; private set; }

    // CommercialTerms VO fields (DOMAIN-MODEL.md §5.5) - all technical envelope, see class doc comment.
    public string? CurrencyCode { get; private set; }
    public string? PaymentTerms { get; private set; }
    public string? IncotermCode { get; private set; }
    public string? DeliveryTermsAr { get; private set; }
    public string? DeliveryTermsEn { get; private set; }
    public string? Warranty { get; private set; }
    public DateOnly? ValidityStart { get; private set; }
    public DateOnly? ValidityEnd { get; private set; }

    // TechnicalResponse's free narrative half (the other half is RequirementAnswer[]).
    public string? NarrativeAr { get; private set; }
    public string? NarrativeEn { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }
    public DateTimeOffset? WithdrawnAt { get; private set; }
    public string? WithdrawReason { get; private set; }

    /// <summary>§4.1's "Reason; specific questions" on ClarificationRequested.</summary>
    public string? ClarificationReason { get; private set; }

    public DateTimeOffset? ClarificationRequestedAt { get; private set; }

    /// <summary>§4.1's "New revision n+1". Starts at 1 for the original submission.</summary>
    public int RevisionNumber { get; private set; } = 1;
    public DateTimeOffset CreatedAt { get; private init; }
    public uint RowVersion { get; private set; }

    public IReadOnlyList<ProposalItem> Items => _items;
    public IReadOnlyList<ProposalDocument> Documents => _documents;
    public IReadOnlyList<RequirementAnswer> RequirementAnswers => _requirementAnswers;

    private Proposal() { }

    /// <summary>FEAT-09.1/FR-PRP-001: BUSINESS-PROCESSES.md §4.1 "start proposal" guard - "Supplier
    /// is Active and holds a valid Invitation to this RFQ; no existing proposal for this RFQ
    /// (uniqueness)". Active/Invitation are cross-aggregate (handler's job, same split as
    /// Rfq.InviteSupplier's own Active check); uniqueness is enforced by the handler checking for an
    /// existing row first (idempotent start, per FEAT-09.1's own AC: "a second start returns the
    /// existing one") plus a DB unique(rfq_id, supplier_id) constraint as the real guarantee.</summary>
    public static Proposal Create(string referenceCode, Guid rfqId, Guid supplierId) => new()
    {
        Id = Guid.CreateVersion7(),
        ReferenceCode = referenceCode,
        RfqId = rfqId,
        SupplierId = supplierId,
        State = ProposalState.Draft,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>FEAT-09.4/FR-PRP-005: free editing only while Draft (DOMAIN-MODEL.md §5.5's own
    /// invariant) - same "EnsureDraftEditable" shape as Rfq.cs, and the reason a Draft is never
    /// visible to the buyer: nothing outside this aggregate's own owning-supplier query path ever
    /// reads it before Submitted.</summary>
    private void EnsureDraftEditable()
    {
        if (State != ProposalState.Draft)
        {
            throw new DomainException($"Cannot edit this proposal from state '{State}'; only 'Draft' allows edits.");
        }
    }

    /// <summary>Upserts by RfqItemId - re-pricing an already-priced line replaces it rather than
    /// creating a duplicate.</summary>
    public void SetItemPricing(Guid rfqItemId, decimal quantity, decimal unitPrice, decimal? discount, int? leadTimeDays, string? notesAr, string? notesEn)
    {
        EnsureDraftEditable();
        if (quantity <= 0) throw new DomainException("Quantity must be positive.");
        // §7.2 documents this rule as PRICE_NON_POSITIVE ("must be greater than zero") and the API
        // validator enforces it. The guard here permitted zero, so the invariant's own home was the
        // laxer of the two: nothing can reach this aggregate except through that endpoint today, but
        // a second write path would have inherited the looser rule silently.
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

    /// <summary>Upserts by RequirementId.</summary>
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

    public ProposalDocument AddDocument(string storageKey, string originalFileName, string contentType, string? caption)
    {
        EnsureDraftEditable();
        var document = new ProposalDocument
        {
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

    /// <summary>FEAT-09.5/FR-PRP-006/007, BUSINESS-PROCESSES.md §4.1: "RFQ SubmissionOpen; now &lt;
    /// submissionCloseAt; all required items priced; mandatory ProposalDocuments attached; Validity
    /// &gt;= RFQ minimum; T&amp;C accepted". <paramref name="rfqSubmissionOpen"/>, <paramref name="submissionCloseAt"/>, and the required/
    /// mandatory id sets are cross-aggregate facts the caller (handler) resolves from the loaded Rfq
    /// - this method is the actual enforcement, not a formality: it is what makes late submission
    /// impossible even with a stale client clock (server-side now, not a client-supplied timestamp).
    ///
    /// <para><b>Two ambiguities, flagged rather than resolved:</b> (1) "mandatory ProposalDocuments
    /// attached" - BUSINESS-PROCESSES.md never defines which documents are mandatory (unlike
    /// Requirements, which carry their own IsMandatory flag); this method does not gate on document
    /// count at all, only on mandatory Requirement answers and required item pricing. (2) "Validity
    /// >= RFQ minimum" - no RFQ field for a minimum validity period exists anywhere in this codebase
    /// (Rfq.cs has no such property); inventing a number would silently resolve an undecided
    /// business rule, so this only enforces that Validity is set and ValidityEnd is not in the past,
    /// not a specific minimum duration - same treatment BRULE-033's undecided minimum submission
    /// window already received in EPIC-07.</para></summary>
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
            throw new DomainException("Cannot submit: all required RFQ items must be priced.");
        }
        var answeredRequirementIds = _requirementAnswers.Select(a => a.RequirementId).ToHashSet();
        if (!mandatoryRequirementIds.IsSubsetOf(answeredRequirementIds))
        {
            throw new DomainException("Cannot submit: all mandatory requirements must be answered.");
        }
        if (ValidityEnd is null)
        {
            throw new DomainException("Cannot submit: a validity end date is required.");
        }
        if (ValidityEnd < DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date))
        {
            throw new DomainException("Cannot submit: the validity end date must not be in the past.");
        }

        State = ProposalState.Submitted;
        SubmittedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>FEAT-09.6/FR-PRP-008, BUSINESS-PROCESSES.md §4.1: "Draft / Submitted -&gt; Withdrawn
    /// ... RFQ still SubmissionOpen (window open)". <paramref name="rfqSubmissionOpen"/> is
    /// cross-aggregate (handler resolves it from the loaded Rfq's own State), same split as
    /// Submit's submissionCloseAt.</summary>
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

    /// <summary>EPIC-14/FEAT-14.4/FR-AWD-004: the winning proposal at award time. Guards on
    /// 'Submitted' rather than 'Shortlisted' - DOMAIN-MODEL.md's own canonical machine routes
    /// through Shortlisted first, but EPIC-13 (the epic that would ever move a proposal into it)
    /// isn't built, so 'Shortlisted' is unreachable by any method on this aggregate today; treating
    /// 'Submitted' as the award-eligible pre-state is this build's real, working substitute for a
    /// stage that doesn't exist yet, not a silent skip of the real guard - eligibility itself is
    /// still fully enforced (Finalized evaluation + passed thresholds), just by the Award aggregate
    /// before it ever calls this method, the same cross-aggregate-guard split used everywhere else
    /// in this codebase.
    ///
    /// <para><b>AwardOffered is skipped</b> - straight to Awarded, no supplier-facing accept/decline
    /// step (BRULE-057/081's active-acceptance flow). Flagged as a real, interim scope decision:
    /// nothing in this build lets a supplier decline an award, so "the winner accepted" is assumed
    /// the instant the award is issued, not observed.</para></summary>

    /// <summary>
    /// T-051, BUSINESS-PROCESSES.md §4.1: <c>Submitted -&gt; UnderReview</c>, <i>"Evaluation opened |
    /// `system` (on RFQ `UnderEvaluation`) | RFQ moved to evaluation | Make visible to assigned
    /// `evaluator`s (scoped)"</i>.
    ///
    /// <para>This is the gateway the whole middle of the lifecycle hung on. Nothing assigned
    /// UnderReview, so nothing could reach ClarificationRequested or Shortlisted either - six of
    /// eleven states were unreachable and a proposal went Draft -&gt; Submitted -&gt; outcome,
    /// skipping evaluation intake entirely.</para>
    ///
    /// <para>System-driven, so there is no permission here: the actor is the RFQ's own transition to
    /// UnderEvaluation, and the caller is the handler that performs it.</para>
    /// </summary>
    public void OpenForReview()
    {
        if (State != ProposalState.Submitted)
        {
            throw new DomainException($"Cannot open for review from state '{State}'; only 'Submitted' is valid.");
        }

        State = ProposalState.UnderReview;
    }

    /// <summary>
    /// §4.1: <c>UnderReview -&gt; ClarificationRequested</c>, <i>"Request clarification |
    /// `procurement_officer`,`evaluator` / `rfq.clarify` | Reason; specific questions"</i>.
    /// </summary>
    /// <param name="reason">Mandatory per the table's own guard - "Reason; specific questions".</param>
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

    /// <summary>
    /// §4.1: <c>ClarificationRequested -&gt; Revised</c>, <i>"Supplier responds | `supplier_admin` /
    /// `proposal.revise` | Within clarification window; only permitted fields changed | New revision
    /// n+1"</i>.
    ///
    /// <para><b>The revision counter is incremented; the snapshot is not taken.</b> §4.1 asks for
    /// "New revision n+1; snapshot" and BRULE-051 for immutable prior revisions. Revision numbering
    /// is implemented here because it is unambiguous; snapshotting a proposal's full prior content
    /// is a storage design nothing in this codebase has, and inventing one inside a transition would
    /// be the larger half of the requirement decided in passing. Recorded rather than half-built.</para>
    ///
    /// <para><b>Scope is NOT enforced here.</b> The table says "only permitted fields changed", and
    /// which fields are permitted is BRULE-050 - a configurable policy whose default is undecided.
    /// A guard would have to invent that policy, so the transition is what exists and the field-level
    /// restriction is not claimed.</para>
    /// </summary>
    public void RecordRevision()
    {
        if (State != ProposalState.ClarificationRequested)
        {
            throw new DomainException($"Cannot revise from state '{State}'; only 'ClarificationRequested' is valid.");
        }

        State = ProposalState.Revised;
        RevisionNumber += 1;
    }

    /// <summary>
    /// §4.1: <c>Revised -&gt; UnderReview</c>, <i>"Re-review | `system`/`procurement_officer` | -- |
    /// Return to scoring"</i>. The loop the table marks as repeatable.
    /// </summary>
    public void ReturnToReview()
    {
        if (State != ProposalState.Revised)
        {
            throw new DomainException($"Cannot return to review from state '{State}'; only 'Revised' is valid.");
        }

        State = ProposalState.UnderReview;
    }

    /// <summary>
    /// §4.1: <c>UnderReview -&gt; Shortlisted</c>, <i>"Passes thresholds |
    /// `procurement_officer`,`procurement_manager` / `evaluation.consolidate` | Consolidated score
    /// &gt;= thresholds (§5)"</i>.
    ///
    /// <para><b>Two documents name different triggers and this follows §4.1.</b> §3.1's RFQ table
    /// says <c>Shortlisting -&gt; Recommendation</c> has the side effect "set proposal(s)
    /// `Shortlisted`" - i.e. at recommendation time, under `award.recommend`. §4.1's proposal table
    /// says at consolidation, under `evaluation.consolidate`. The proposal's own transition table is
    /// the more specific authority for a proposal transition, and consolidation is where the
    /// threshold comparison actually happens. Reported as a documentation conflict rather than
    /// resolved silently.</para>
    /// </summary>
    public void Shortlist()
    {
        if (State != ProposalState.UnderReview)
        {
            throw new DomainException($"Cannot shortlist from state '{State}'; only 'UnderReview' is valid.");
        }

        State = ProposalState.Shortlisted;
    }

    /// <summary>
    /// §3's "allowed next states" for a proposal, from BUSINESS-PROCESSES.md §4.1's own table.
    ///
    /// <para>This is a PROMISE TO A CALLER about what it may attempt next, so it describes what the
    /// code actually accepts rather than only what §4.1 draws. Two places are wider than the
    /// diagram, both deliberately and both pre-existing: Submitted and UnderReview list Awarded and
    /// NotSelected, because this codebase awards directly out of the evaluation set - §4.1's
    /// canonical route is Shortlisted -&gt; AwardOffered -&gt; Awarded, and AwardOffered is not built
    /// (T-064). Listing only the canonical route would tell a caller a transition is unavailable
    /// when the API will in fact perform it.</para>
    /// </summary>
    public static IReadOnlyList<ProposalState> AllowedNextFrom(ProposalState state) => state switch
    {
        ProposalState.Draft => [ProposalState.Submitted, ProposalState.Withdrawn],

        // Withdrawn is reachable from Submitted while the RFQ window is open (§4.1, BRULE-047).
        ProposalState.Submitted =>
            [ProposalState.UnderReview, ProposalState.Withdrawn, ProposalState.Awarded, ProposalState.NotSelected],

        ProposalState.UnderReview =>
            [ProposalState.ClarificationRequested, ProposalState.Shortlisted, ProposalState.NotSelected, ProposalState.Awarded],

        ProposalState.ClarificationRequested => [ProposalState.Revised],
        ProposalState.Revised => [ProposalState.UnderReview],

        // AwardOffered is listed because §4.1 defines it, even though T-064 has not built the
        // transition yet - and Awarded directly, which is what the code does today.
        ProposalState.Shortlisted =>
            [ProposalState.AwardOffered, ProposalState.NotSelected, ProposalState.Awarded],

        ProposalState.AwardOffered => [ProposalState.Awarded, ProposalState.Declined],

        // Terminal.
        ProposalState.Awarded or ProposalState.NotSelected
            or ProposalState.Declined or ProposalState.Withdrawn => [],

        _ => [],
    };

    public void Award()
    {
        // Submitted stays valid, and Shortlisted joins it (§4.1: Shortlisted -> AwardOffered ->
        // Awarded). Widened rather than replaced: making the middle of the lifecycle reachable must
        // not break the award path for an RFQ that never went through evaluation intake, and both
        // routes exist in the documents.
        // UnderReview is here because this codebase awards directly out of the evaluation set:
        // §4.1's canonical path is Shortlisted -> AwardOffered -> Awarded, but AwardOffered is not
        // built (see the class note), so the winner is whatever state intake left it in. Omitting it
        // produced an uncaught DomainException and a 500 on award/execute, because the winner is
        // UnderReview from the moment T-051 made intake work.
        if (State is not (ProposalState.Submitted or ProposalState.UnderReview or ProposalState.Shortlisted))
        {
            throw new DomainException(
                $"Cannot award from state '{State}'; only 'Submitted', 'UnderReview' or 'Shortlisted' is valid.");
        }
        State = ProposalState.Awarded;
    }

    /// <summary>EPIC-14/FEAT-14.4/FR-AWD-004: every other Submitted proposal on the RFQ, moved in
    /// the same handler call/SaveChanges as the winner's Award() - see AwardHandlers' own doc
    /// comment on why this must never leave a window where some proposals are updated and others
    /// aren't.</summary>
    public void MarkNotSelected()
    {
        // §4.1: "UnderReview / Shortlisted -> NotSelected". Submitted is kept for the pre-evaluation
        // award path that already existed.
        if (State is not (ProposalState.Submitted or ProposalState.UnderReview or ProposalState.Shortlisted))
        {
            throw new DomainException(
                $"Cannot mark not-selected from state '{State}'; only 'Submitted', 'UnderReview' or 'Shortlisted' is valid.");
        }
        State = ProposalState.NotSelected;
    }
}
