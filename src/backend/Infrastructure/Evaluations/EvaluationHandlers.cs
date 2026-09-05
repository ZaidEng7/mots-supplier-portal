using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Evaluations;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;
using EvaluationAggregate = MotsSupplierPortal.Domain.Evaluation.Evaluation;

namespace MotsSupplierPortal.Infrastructure.Evaluations;

internal static class EvaluationDtoMapper
{
    public static EvaluationDto ToDto(EvaluationAggregate evaluation, Rfq rfq) => new(
        evaluation.Id, evaluation.RfqId, rfq.ReferenceCode, evaluation.State,
        [.. evaluation.Criteria.Select(ToCriterionDto)],
        [.. evaluation.Assignments.Select(a => new EvaluationAssignmentDto(a.EvaluatorUserId, a.AssignedAt, a.SubmittedAt, a.RecusedAt, a.RecusalReason))],
        [.. evaluation.Results.Select(r => new ConsolidatedResultDto(r.ProposalId, r.TechnicallyQualified, r.TechnicalWeightedScore, r.FinancialWeightedScore, r.WeightedTotal, r.Rank, r.TieUnresolved, r.TieResolutionReason))],
        evaluation.RowVersion);

    public static EvaluationCriterionDto ToCriterionDto(EvaluationCriterionSnapshot c) =>
        new(c.Id, c.NameAr, c.NameEn, c.Dimension, c.Weight, c.MaxScore, c.Threshold, c.ScoringType, c.IsFinancial,
            c.RequiresJustification);

    /// <summary>
    /// T-067: the evaluator's workspace, with the bids and the specification on it.
    ///
    /// <para><paramref name="bids"/> arrives already loaded and already filtered to the technical
    /// envelope - this method does no querying, so there is no path by which a pricing row could be
    /// pulled in here by a later edit.</para>
    /// </summary>
    public static MyEvaluationDto ToMyDto(
        EvaluationAggregate evaluation, Rfq rfq, Guid evaluatorUserId, IReadOnlyList<EvaluatorBid> bids)
    {
        var assignment = evaluation.Assignments.First(a => a.EvaluatorUserId == evaluatorUserId && a.IsActive);
        var codeById = bids.ToDictionary(b => b.ProposalId, b => b.ProposalCode);

        var myScores = evaluation.Scores.Where(s => s.EvaluatorUserId == evaluatorUserId)
            // A score whose proposal is no longer in evaluation (withdrawn mid-scoring) has no code
            // to name it by, and showing a bid that left is worse than omitting it.
            .Where(s => codeById.ContainsKey(s.ProposalId))
            .Select(s => new MyScoreDto(codeById[s.ProposalId], s.CriterionId, s.RawScore, s.CommentAr, s.CommentEn, s.ScoredAt))
            .ToList();

        // A-8: bidders are anonymous WHILE this evaluator is scoring, and named at the two moments
        // where the name is the point.
        //
        // Before scoring opens, the evaluator is looking at the assignment they have been offered and
        // declaring conflicts - BRULE-067's recusal, which is an assignment-time act. After
        // consolidation the scores are in and locked, so a name can no longer influence one.
        //
        // Between those two, the name is withheld and a stable pseudonym stands in its place. Keyed on
        // THIS evaluator's own submission rather than on the evaluation's state, because the two
        // evaluators on a committee are not necessarily at the same point.
        // Revealed in exactly two windows, and the first is keyed on THIS evaluator's declaration rather
        // than on the evaluation's state: reading my-evaluation is itself what opens scoring (see
        // GetMyEvaluationHandler), and the evaluation goes InProgress when the FIRST evaluator opens it -
        // so a state-only rule would close the second evaluator's declaration window before they had
        // one, and would reveal names to whoever happened to look first.
        //
        //   1. before this evaluator has declared - the recusal window (BRULE-067), served by
        //      GET my-evaluation/bidders, which does NOT open scoring;
        //   2. after consolidation - the scores are in and locked, so a name cannot influence one.
        var revealed = assignment.ConflictDeclaredAt is null
            || evaluation.State is EvaluationState.Consolidated or EvaluationState.Finalized;

        // Ordered by proposal code so the label for a given bid is the same on every read and for every
        // evaluator - a committee cannot discuss "Bidder B" if it means a different bid to each member.
        var labels = bids
            .OrderBy(b => b.ProposalCode, StringComparer.Ordinal)
            .Select((b, index) => (b.ProposalId, Index: index))
            .ToDictionary(x => x.ProposalId, x => x.Index);

        var proposals = bids.Select(b => new EvaluatorProposalDto(
            b.ProposalCode,
            BidderLabel.Arabic(labels[b.ProposalId]),
            BidderLabel.English(labels[b.ProposalId]),
            revealed ? b.SupplierReferenceCode : null,
            revealed ? b.SupplierDisplayNameAr : null,
            revealed ? b.SupplierDisplayNameEn : null,
            b.NarrativeAr, b.NarrativeEn, b.RequirementAnswers, b.Documents,
            evaluation.IsTechnicallyQualifiedByEvaluator(evaluatorUserId, b.ProposalId))).ToList();

        return new MyEvaluationDto(
            rfq.ReferenceCode, evaluation.State,
            rfq.TitleAr, rfq.TitleEn, rfq.DescriptionAr, rfq.DescriptionEn,
            [.. rfq.Items.OrderBy(i => i.LineNo).Select(i => new RfqItemDto(
                i.Id, i.LineNo, i.TitleAr, i.TitleEn, i.SpecificationAr, i.SpecificationEn,
                i.CategoryCode, i.Quantity, i.UnitOfMeasureCode, i.IsUnitPrice, i.IsOptional))],
            [.. rfq.Requirements.Select(r => new RequirementDto(r.Id, r.TextAr, r.TextEn, r.IsMandatory, r.DocumentTypeCode))],
            assignment.SubmittedAt, [.. evaluation.Criteria.Select(ToCriterionDto)], proposals, myScores);
    }
}

/// <summary>
/// A-8: the pseudonym an anonymised bid is known by while scoring is open.
///
/// <para>Letters, not numbers, and deliberately: a number reads as a rank, and the whole point is that
/// the evaluator does not yet know which bid is better. Arabic uses the abjad letter sequence
/// (أ ب ج د …) rather than the alphabetical one, which is what an Arabic reader expects for
/// enumeration. Past the end of either alphabet it falls back to a two-part label rather than throwing
/// - an evaluation with 27 bids is unlikely and would still have to render.</para>
/// </summary>
internal static class BidderLabel
{
    private const string EnglishLetters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private static readonly string[] ArabicAbjad =
        ["أ", "ب", "ج", "د", "هـ", "و", "ز", "ح", "ط", "ي", "ك", "ل", "م", "ن", "س", "ع", "ف", "ص", "ق", "ر", "ش", "ت", "ث", "خ", "ذ", "ض", "ظ", "غ"];

    public static string English(int index) =>
        index < EnglishLetters.Length
            ? $"Bidder {EnglishLetters[index]}"
            : $"Bidder {index + 1}";

    public static string Arabic(int index) =>
        index < ArabicAbjad.Length
            ? $"مورّد {ArabicAbjad[index]}"
            : $"مورّد {index + 1}";
}

file static class EvaluationLoader
{
    public static IQueryable<EvaluationAggregate> IncludeAll(this DbSet<EvaluationAggregate> set) =>
        set.Include(e => e.Criteria).Include(e => e.Assignments).Include(e => e.Scores).Include(e => e.Results).AsSplitQuery();

    /// <summary>Buyer-side: scoped to the caller's own Organization via the bound Rfq, same shape
    /// as RfqLoader.LoadScopedAsync.</summary>
    public static async Task<(Rfq Rfq, EvaluationAggregate Evaluation)?> LoadScopedByOrgAsync(AppDbContext db, IScopeContext scope, string rfqReferenceCode, CancellationToken ct)
    {
        if (scope.OrganizationId is null) return null;
        var rfq = await db.Rfqs.FirstOrDefaultAsync(r => r.ReferenceCode == rfqReferenceCode && r.OrganizationId == scope.OrganizationId, ct);
        if (rfq is null) return null;
        var evaluation = await db.Evaluations.IncludeAll().FirstOrDefaultAsync(e => e.RfqId == rfq.Id, ct);
        return evaluation is null ? null : (rfq, evaluation);
    }

    /// <summary>Evaluator-side: scoped to "this caller holds an active assignment on this
    /// evaluation" - deliberately NOT to OrganizationId (an evaluator need not belong to the
    /// procuring organization).</summary>
    public static async Task<(Rfq Rfq, EvaluationAggregate Evaluation)?> LoadScopedByAssignmentAsync(AppDbContext db, IScopeContext scope, string rfqReferenceCode, CancellationToken ct)
    {
        if (scope.UserId is null) return null;
        var rfq = await db.Rfqs.FirstOrDefaultAsync(r => r.ReferenceCode == rfqReferenceCode, ct);
        if (rfq is null) return null;
        var evaluation = await db.Evaluations.IncludeAll().FirstOrDefaultAsync(e => e.RfqId == rfq.Id, ct);
        if (evaluation is null) return null;
        if (!evaluation.Assignments.Any(a => a.EvaluatorUserId == scope.UserId && a.IsActive)) return null;
        return (rfq, evaluation);
    }

    public static Task<List<Guid>> SubmittedProposalIdsAsync(AppDbContext db, Guid rfqId, CancellationToken ct) =>
        db.Proposals.Where(p => p.RfqId == rfqId && ProposalStates.InEvaluation.Contains(p.State)).Select(p => p.Id).ToListAsync(ct);

    /// <summary>
    /// T-067: every bid under evaluation on this RFQ, projected to its TECHNICAL envelope.
    ///
    /// <para><b>The seal is the projection, not a filter applied afterwards.</b> This is a
    /// <c>Select</c> in SQL that never names <c>ProposalItem</c>, <c>CurrencyCode</c>,
    /// <c>PaymentTerms</c> or any other commercial column - so no pricing row is loaded into memory
    /// for an evaluator to leak by accident, and adding one would mean editing this projection
    /// rather than forgetting a filter. Same reasoning as ProposalDtoMapper's note on why the
    /// two-envelope seal was "not a filter applied to a shared read".</para>
    ///
    /// <para>Documents are Technical-envelope only (D-7). They are NOT filtered on scan state, and
    /// that is a correction to this method's first version: proposal documents are scanned on first
    /// ACCESS (D-10), so nothing scans them until a download happens - filtering the list to Clean
    /// made it permanently empty, in production as well as in the test that caught it. PendingScan
    /// means "not yet examined", not "suspect"; listing a file is not serving it, and the download
    /// route still scans and still refuses. See DECISIONS-TAKEN.md D-20.</para>
    /// </summary>
    public static Task<List<EvaluatorBid>> EvaluatorBidsAsync(AppDbContext db, Guid rfqId, CancellationToken ct) =>
        db.Proposals
            .Where(p => p.RfqId == rfqId && ProposalStates.InEvaluation.Contains(p.State))
            .OrderBy(p => p.ReferenceCode)
            .Select(p => new EvaluatorBid(
                p.Id,
                p.ReferenceCode,
                db.Suppliers.Where(s => s.Id == p.SupplierId).Select(s => s.ReferenceCode).First(),
                db.Suppliers.Where(s => s.Id == p.SupplierId).Select(s => s.DisplayNameAr).First(),
                db.Suppliers.Where(s => s.Id == p.SupplierId).Select(s => s.DisplayNameEn).First(),
                p.NarrativeAr,
                p.NarrativeEn,
                p.RequirementAnswers
                    .Select(a => new RequirementAnswerDto(a.Id, a.RequirementId, a.AnswerAr, a.AnswerEn))
                    .ToList(),
                p.Documents
                    .Where(d => d.Envelope == ProposalDocumentEnvelope.Technical
                                && d.ScanState != AttachmentScanState.ScanRejected)
                    .OrderBy(d => d.UploadedAt)
                    .Select(d => new EvaluatorProposalDocumentDto(
                        d.Id, d.OriginalFileName, d.ContentType, d.Caption, d.UploadedAt))
                    .ToList()))
            .ToListAsync(ct);
}

/// <summary>The loaded technical envelope of one bid. Internal to Infrastructure - the GUID stays on
/// this side of the boundary and never reaches EvaluatorProposalDto.</summary>
internal sealed record EvaluatorBid(
    Guid ProposalId, string ProposalCode,
    string SupplierReferenceCode, string SupplierDisplayNameAr, string SupplierDisplayNameEn,
    string? NarrativeAr, string? NarrativeEn,
    IReadOnlyList<RequirementAnswerDto> RequirementAnswers,
    IReadOnlyList<EvaluatorProposalDocumentDto> Documents);

/// <summary>FEAT-11.2/FR-EVL-001, BUSINESS-PROCESSES.md §5.1: "SubmissionClosed -&gt;
/// UnderEvaluation ... system (on RFQ UnderEvaluation) ... Instantiate criteria from
/// EvaluationTemplate; snapshot weights". The RFQ's own SubmissionClosed -&gt; UnderEvaluation
/// transition and the Evaluation's creation happen in the same handler/SaveChangesAsync call -
/// same pragmatic single-unit-of-work exception BindEvaluationTemplateHandler's own doc comment
/// already justifies, not a new pattern.</summary>
public sealed class OpenEvaluationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IOpenEvaluationHandler
{
    private sealed record CriterionSnapshotJson(
        Guid Id, string NameAr, string NameEn, string Dimension, decimal Weight, decimal MaxScore, decimal? Threshold,
        string ScoringType,
        // Defaults to false for an RFQ whose snapshot predates the field - the same reason Criterion's
        // own flag defaults false rather than being backfilled from a rule nobody had stated yet.
        bool RequiresJustification = false);

    public async Task<EvaluationMutationResult> HandleAsync(OpenEvaluationCommand command, CancellationToken ct)
    {
        if (scope.OrganizationId is null) return new EvaluationMutationResult.NotFoundOrOutOfScope();
        var rfq = await db.Rfqs.FirstOrDefaultAsync(r => r.ReferenceCode == command.RfqReferenceCode && r.OrganizationId == scope.OrganizationId, ct);
        if (rfq is null) return new EvaluationMutationResult.NotFoundOrOutOfScope();

        if (rfq.EvaluationTemplateSnapshotJson is null)
        {
            return new EvaluationMutationResult.InvalidState("Cannot open evaluation: no evaluation template is bound to this RFQ.");
        }
        // BUSINESS-PROCESSES.md §5.1 guard "&gt;=1 Submitted proposal [ASSUMPTION]" - cross-aggregate
        // (Proposal lives in a different aggregate), resolved here before the domain transition,
        // same split as every other cross-aggregate guard in this codebase.
        var submittedCount = await db.Proposals.CountAsync(p => p.RfqId == rfq.Id && p.State == ProposalState.Submitted, ct);
        if (submittedCount == 0)
        {
            return new EvaluationMutationResult.InvalidState("Cannot open evaluation: at least one Submitted proposal is required.");
        }

        var fromState = rfq.State;
        try
        {
            rfq.OpenEvaluation();
        }
        catch (DomainException ex)
        {
            return new EvaluationMutationResult.InvalidState(ex.Message);
        }

        // T-051, FR-PRP-009, §4.1: "Submitted -> UnderReview | Evaluation opened | system (on RFQ
        // UnderEvaluation) | Make visible to assigned evaluators (scoped)".
        //
        // This is the gateway the whole middle of the proposal lifecycle hung on. Nothing assigned
        // UnderReview, so ClarificationRequested, Revised and Shortlisted were unreachable too - a
        // proposal went Draft -> Submitted -> outcome and skipped evaluation intake entirely.
        //
        // In the SAME SaveChanges as the RFQ's own transition, because a window where the RFQ is
        // UnderEvaluation and its proposals are still Submitted is a state no document describes.
        var intake = await db.Proposals
            .Where(p => p.RfqId == rfq.Id && p.State == ProposalState.Submitted)
            .ToListAsync(ct);

        foreach (var proposal in intake)
        {
            proposal.OpenForReview();
            await auditLogger.LogAsync("Proposal", proposal.Id, "proposal_under_review", scope.UserId,
                referenceCode: proposal.ReferenceCode,
                fromState: nameof(ProposalState.Submitted), toState: nameof(ProposalState.UnderReview), ct: ct);
        }

        var criteriaJson = JsonSerializer.Deserialize<List<CriterionSnapshotJson>>(rfq.EvaluationTemplateSnapshotJson)!;
        var criteriaInputs = criteriaJson.Select(c => new CriterionSnapshotInput(
            c.NameAr, c.NameEn, Enum.Parse<CriterionDimension>(c.Dimension), c.Weight, c.MaxScore, c.Threshold,
            Enum.Parse<ScoringType>(c.ScoringType), c.RequiresJustification)).ToList();

        var evaluation = EvaluationAggregate.Create(rfq.Id, criteriaInputs);
        db.Evaluations.Add(evaluation);

        // §3.1 "SubmissionClosed -> UnderEvaluation | In-app to `evaluator`s". No assignments exist
        // yet at this moment, so this is the committee that runs the RFQ rather than a list of
        // assignees - the assignment notification (§3.3 "NotStarted -> Assigned") is the one that
        // reaches individual evaluators, and it already exists as an email.
        NotificationOutbox.EnqueueMany(db, NotificationTypes.EvaluationOpened,
            await NotificationRecipients.CommitteeAsync(db, rfq.OrganizationId, ct),
            $"{NotificationTypes.EvaluationOpened}:{rfq.Id}",
            new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["rfqId"] = rfq.Id.ToString() });

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_evaluation_opened", scope.UserId, referenceCode: rfq.ReferenceCode,
            fromState: fromState.ToString(), toState: nameof(RfqState.UnderEvaluation), ct: ct);
        await auditLogger.LogAsync("Evaluation", evaluation.Id, "evaluation_created", scope.UserId, referenceCode: rfq.ReferenceCode,
            toState: nameof(EvaluationState.NotStarted), ct: ct);
        await db.SaveChangesAsync(ct);
        return new EvaluationMutationResult.Success(EvaluationDtoMapper.ToDto(evaluation, rfq));
    }
}

public sealed class GetEvaluationHandler(AppDbContext db, IScopeContext scope) : IGetEvaluationHandler
{
    public async Task<EvaluationDto?> HandleAsync(string rfqReferenceCode, CancellationToken ct)
    {
        var loaded = await EvaluationLoader.LoadScopedByOrgAsync(db, scope, rfqReferenceCode, ct);
        return loaded is null ? null : EvaluationDtoMapper.ToDto(loaded.Value.Evaluation, loaded.Value.Rfq);
    }
}

/// <summary>FEAT-11.2/FR-EVL-001, BUSINESS-PROCESSES.md §5.1: "— -&gt; Assigned ... procurement_manager
/// / evaluation.assign". Assigns every candidate to every Submitted proposal on the RFQ - see
/// EvaluationAssignment.cs's own doc comment on why no per-evaluator proposal subset exists.</summary>
/// <summary>FEAT-13.3 audit gap fix: notifies each newly-assigned evaluator - previously they only
/// learned of the assignment by independently checking their own evaluation dashboard.</summary>
public sealed class AssignEvaluatorsHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger, IBackgroundJobClient backgroundJobs) : IAssignEvaluatorsHandler
{
    public async Task<EvaluationMutationResult> HandleAsync(AssignEvaluatorsCommand command, CancellationToken ct)
    {
        var loaded = await EvaluationLoader.LoadScopedByOrgAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null) return new EvaluationMutationResult.NotFoundOrOutOfScope();
        var (rfq, evaluation) = loaded.Value;

        var existingAssignmentIds = evaluation.Assignments.Select(a => a.Id).ToHashSet();
        try
        {
            evaluation.AssignEvaluators(command.EvaluatorUserIds);
        }
        catch (DomainException ex)
        {
            return new EvaluationMutationResult.InvalidState(ex.Message);
        }
        // EF's change-tracker misclassifies a brand-new child appended to an already-Included
        // collection as Modified (not Added) when the SAME SaveChanges also updates the owning
        // Evaluation row's own State column (NotStarted -&gt; Assigned here) - forcing the state
        // explicitly for the rows this call actually created sidesteps that misdetection rather
        // than relying on DetectChanges' fixup heuristic to get it right.
        foreach (var assignment in evaluation.Assignments.Where(a => !existingAssignmentIds.Contains(a.Id)))
        {
            db.Entry(assignment).State = EntityState.Added;
        }

        await auditLogger.LogAsync("Evaluation", evaluation.Id, "evaluation_evaluators_assigned", scope.UserId,
            referenceCode: rfq.ReferenceCode, toState: string.Join(",", command.EvaluatorUserIds), ct: ct);
        await db.SaveChangesAsync(ct);

        foreach (var evaluatorUserId in command.EvaluatorUserIds)
        {
            backgroundJobs.Enqueue<EmailJobs>(job => job.SendEvaluatorAssignedEmailAsync(evaluatorUserId, rfq.Id, CancellationToken.None));
        }

        return new EvaluationMutationResult.Success(EvaluationDtoMapper.ToDto(evaluation, rfq));
    }
}

/// <summary>BRULE-067, and FEAT-11.7/FR-EVL-011's non-responding-evaluator tool (see
/// Evaluation.cs's own class doc comment on why no separate quorum/exclude action exists).</summary>
public sealed class RecuseEvaluatorHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IRecuseEvaluatorHandler
{
    public async Task<EvaluationMutationResult> HandleAsync(RecuseEvaluatorCommand command, CancellationToken ct)
    {
        var loaded = await EvaluationLoader.LoadScopedByOrgAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null) return new EvaluationMutationResult.NotFoundOrOutOfScope();
        var (rfq, evaluation) = loaded.Value;

        try
        {
            evaluation.RecuseEvaluator(command.EvaluatorUserId, command.Reason);
        }
        catch (DomainException ex)
        {
            return new EvaluationMutationResult.InvalidState(ex.Message);
        }

        // §3.3 has no row for recusal - it is a within-state event, not a transition. Notifying the
        // officer who owns the RFQ is the defensible reading, and it is flagged as such in the
        // catalogue rather than presented as transcribed. A-7 makes "the officer" a person: a
        // recusal leaves the evaluation short an evaluator, and somebody has to replace them.
        NotificationOutbox.EnqueueMany(db, NotificationTypes.EvaluatorRecused,
            await NotificationRecipients.RfqOwnerAsync(db, rfq, ct),
            $"{NotificationTypes.EvaluatorRecused}:{evaluation.Id}:{command.EvaluatorUserId}",
            new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["evaluationId"] = evaluation.Id.ToString() });

        await auditLogger.LogAsync("Evaluation", evaluation.Id, "evaluation_evaluator_recused", scope.UserId,
            referenceCode: rfq.ReferenceCode, toState: command.EvaluatorUserId.ToString(), reason: command.Reason, ct: ct);
        await db.SaveChangesAsync(ct);
        return new EvaluationMutationResult.Success(EvaluationDtoMapper.ToDto(evaluation, rfq));
    }
}

/// <summary>FEAT-11.6/FR-EVL-007, procurement_officer,procurement_manager / evaluation.consolidate.</summary>
public sealed class ConsolidateEvaluationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IConsolidateEvaluationHandler
{
    public async Task<EvaluationMutationResult> HandleAsync(ConsolidateEvaluationCommand command, CancellationToken ct)
    {
        var loaded = await EvaluationLoader.LoadScopedByOrgAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null) return new EvaluationMutationResult.NotFoundOrOutOfScope();
        var (rfq, evaluation) = loaded.Value;

        var existingResultIds = evaluation.Results.Select(r => r.Id).ToHashSet();

        // A-1/BRULE-069: the two tie-break rungs the scores cannot supply. Loaded here because the
        // aggregate has no access to proposals, and materialised before summing because a SQL SUM over
        // a computed property does not translate (the lesson from EPIC-18's awarded-value tile).
        //
        // Reading the commercial total at CONSOLIDATION is not a two-envelope breach: OQ-009's seal is
        // between the technical and commercial envelopes DURING scoring, and consolidation is where
        // the financial dimension is deliberately brought in.
        var bids = await db.Proposals
            .Where(p => p.RfqId == rfq.Id)
            .Select(p => new { p.Id, p.SubmittedAt, Items = p.Items.Select(i => new { i.Quantity, i.UnitPrice, i.Discount }).ToList() })
            .ToListAsync(ct);
        var bidFacts = bids.ToDictionary(
            b => b.Id,
            b => new Domain.Evaluation.Evaluation.BidTieBreakFacts(
                b.Items.Count == 0 ? null : b.Items.Sum(i => (i.Quantity * i.UnitPrice) - (i.Discount ?? 0m)),
                b.SubmittedAt));

        try
        {
            evaluation.Consolidate(bidFacts);
        }
        catch (DomainException ex)
        {
            return new EvaluationMutationResult.InvalidState(ex.Message);
        }
        // See AssignEvaluatorsHandler's own comment on why this is forced explicitly rather than
        // left to DetectChanges' fixup heuristic.
        foreach (var result in evaluation.Results.Where(r => !existingResultIds.Contains(r.Id)))
        {
            db.Entry(result).State = EntityState.Added;
        }

        // T3-36. §3.1: "UnderEvaluation | Shortlisting | Begin shortlisting |
        // `procurement_officer`,`procurement_manager` / `evaluation.consolidate` | Evaluation
        // Consolidated/Finalized". The permission the table names for that transition is THIS
        // operation's permission, and the guard it names is this operation's outcome - so
        // consolidating is the trigger, rather than a second endpoint an officer would have to
        // remember to call to keep the RFQ's state honest.
        if (rfq.State == RfqState.UnderEvaluation)
        {
            rfq.BeginShortlisting();

            NotificationOutbox.EnqueueMany(db, NotificationTypes.RfqShortlistingStarted,
                await NotificationRecipients.CommitteeAsync(db, rfq.OrganizationId, ct),
                $"{NotificationTypes.RfqShortlistingStarted}:{rfq.Id}",
                new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["rfqId"] = rfq.Id.ToString() });

            await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_shortlisting_started", scope.UserId,
                referenceCode: rfq.ReferenceCode, fromState: nameof(RfqState.UnderEvaluation),
                toState: nameof(RfqState.Shortlisting), ct: ct);
        }

        // T-051, §4.1: "UnderReview -> Shortlisted | Passes thresholds |
        // procurement_officer,procurement_manager / evaluation.consolidate | Consolidated score >=
        // thresholds (§5)". Same trigger and same permission as the RFQ-level transition above, so
        // shortlisting a proposal is part of consolidating rather than a second action.
        //
        // TechnicallyQualified IS the threshold comparison §4.1 points at - it is what consolidation
        // computes from the criteria's own thresholds, so this reads the result rather than
        // re-deriving a rule.
        //
        // Proposals that do NOT pass are left in UnderReview, not moved to NotSelected. §4.1 puts
        // NotSelected under award.recommend ("Award decided for another / fails threshold"), which is
        // a later decision by a person; marking them here would pre-empt it.
        var qualified = evaluation.Results.Where(r => r.TechnicallyQualified).Select(r => r.ProposalId).ToHashSet();
        if (qualified.Count > 0)
        {
            var toShortlist = await db.Proposals
                .Where(p => p.RfqId == rfq.Id && p.State == ProposalState.UnderReview)
                .ToListAsync(ct);

            foreach (var proposal in toShortlist.Where(p => qualified.Contains(p.Id)))
            {
                proposal.Shortlist();
                await auditLogger.LogAsync("Proposal", proposal.Id, "proposal_shortlisted", scope.UserId,
                    referenceCode: proposal.ReferenceCode,
                    fromState: nameof(ProposalState.UnderReview), toState: nameof(ProposalState.Shortlisted), ct: ct);
            }
        }

        // §3.3 "EvaluatorSubmitted -> Consolidated | In-app to committee".
        NotificationOutbox.EnqueueMany(db, NotificationTypes.EvaluationConsolidated,
            await NotificationRecipients.CommitteeAsync(db, rfq.OrganizationId, ct),
            $"{NotificationTypes.EvaluationConsolidated}:{evaluation.Id}",
            new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["evaluationId"] = evaluation.Id.ToString() });

        await auditLogger.LogAsync("Evaluation", evaluation.Id, "evaluation_consolidated", scope.UserId,
            referenceCode: rfq.ReferenceCode, toState: nameof(EvaluationState.Consolidated), ct: ct);
        await db.SaveChangesAsync(ct);
        return new EvaluationMutationResult.Success(EvaluationDtoMapper.ToDto(evaluation, rfq));
    }
}

/// <summary>FEAT-11.6/FR-EVL-008, procurement_manager / evaluation.finalize - the real Phase 7 exit
/// gate: unlocks RFQ shortlisting/recommendation (BUSINESS-PROCESSES.md §3), though this build does
/// not implement that next RFQ transition itself (EPIC-13/14 territory) - see class doc comment on
/// EvaluationAggregate.FinalizeEvaluation.</summary>
public sealed class FinalizeEvaluationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IFinalizeEvaluationHandler
{
    public async Task<EvaluationMutationResult> HandleAsync(FinalizeEvaluationCommand command, CancellationToken ct)
    {
        var loaded = await EvaluationLoader.LoadScopedByOrgAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null) return new EvaluationMutationResult.NotFoundOrOutOfScope();
        var (rfq, evaluation) = loaded.Value;

        // EPIC-13/FEAT-13.2 stage-gate audit: BUSINESS-PROCESSES.md §5.1's own "Result reviewed;
        // no unresolved clarification" guard for this transition was never enforced anywhere -
        // Evaluation.FinalizeEvaluation's own doc comment already flagged this as a known,
        // deliberate gap left for whichever epic could reach across to Clarification (this one).
        // Clarification is a child entity of Rfq (Domain/Rfqs/Clarification.cs), not its own
        // aggregate, so this is a plain cross-aggregate-adjacent count query, same shape as every
        // other cross-aggregate guard in this codebase.
        var unresolvedClarifications = await db.Clarifications.CountAsync(c => c.RfqId == rfq.Id && c.Answer == null, ct);
        if (unresolvedClarifications > 0)
        {
            return new EvaluationMutationResult.InvalidState(
                $"Cannot finalize the evaluation: {unresolvedClarifications} clarification question(s) are still unanswered.");
        }

        try
        {
            evaluation.FinalizeEvaluation();
        }
        catch (DomainException ex)
        {
            return new EvaluationMutationResult.InvalidState(ex.Message);
        }

        // §3.3 "Consolidated -> Finalized | In-app to committee".
        NotificationOutbox.EnqueueMany(db, NotificationTypes.EvaluationFinalized,
            await NotificationRecipients.CommitteeAsync(db, rfq.OrganizationId, ct),
            $"{NotificationTypes.EvaluationFinalized}:{evaluation.Id}",
            new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["evaluationId"] = evaluation.Id.ToString() });

        await auditLogger.LogAsync("Evaluation", evaluation.Id, "evaluation_finalized", scope.UserId,
            referenceCode: rfq.ReferenceCode, toState: nameof(EvaluationState.Finalized), ct: ct);
        await db.SaveChangesAsync(ct);
        return new EvaluationMutationResult.Success(EvaluationDtoMapper.ToDto(evaluation, rfq));
    }
}

public sealed class ReopenEvaluationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IReopenEvaluationHandler
{
    public async Task<EvaluationMutationResult> HandleAsync(ReopenEvaluationCommand command, CancellationToken ct)
    {
        var loaded = await EvaluationLoader.LoadScopedByOrgAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null) return new EvaluationMutationResult.NotFoundOrOutOfScope();
        var (rfq, evaluation) = loaded.Value;

        try
        {
            evaluation.ReopenForClarification(command.Reason);
        }
        catch (DomainException ex)
        {
            return new EvaluationMutationResult.InvalidState(ex.Message);
        }

        // §3.3 "Consolidated -> InProgress | In-app to affected evaluators" - the assigned, non-recused
        // evaluators, which is what "affected" means here.
        NotificationOutbox.EnqueueMany(db, NotificationTypes.EvaluationReopened,
            await NotificationRecipients.AssignedEvaluatorsAsync(db, evaluation.Id, ct),
            $"{NotificationTypes.EvaluationReopened}:{evaluation.Id}:{DateTimeOffset.UtcNow.Ticks}",
            new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["evaluationId"] = evaluation.Id.ToString() });

        await auditLogger.LogAsync("Evaluation", evaluation.Id, "evaluation_reopened", scope.UserId,
            referenceCode: rfq.ReferenceCode, toState: nameof(EvaluationState.InProgress), reason: command.Reason, ct: ct);
        await db.SaveChangesAsync(ct);
        return new EvaluationMutationResult.Success(EvaluationDtoMapper.ToDto(evaluation, rfq));
    }
}

/// <summary>The blind-scoring read path (OQ-005/BRULE-058): filters every EvaluatorScore to
/// `EvaluatorUserId == scope.UserId` before it ever leaves the handler - see
/// EvaluationDtoMapper.ToMyDto's own filter.</summary>
public sealed class GetMyEvaluationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IGetMyEvaluationHandler
{
    public async Task<MyEvaluationResult> HandleAsync(string rfqReferenceCode, CancellationToken ct)
    {
        var loaded = await EvaluationLoader.LoadScopedByAssignmentAsync(db, scope, rfqReferenceCode, ct);
        if (loaded is null) return new MyEvaluationResult.NotFoundOrNotAssigned();
        var (rfq, evaluation) = loaded.Value;

        // EPIC-13/FEAT-13.3 audit finding: this GET was a real state mutation (Assigned/NotStarted
        // -> InProgress, "the first evaluator to open" per BUSINESS-PROCESSES.md §5.1) with zero
        // audit logging - IAuditLogger wasn't even injected. fromState captured before the call so
        // the audit row is only written when a transition genuinely happened, not on every
        // subsequent GET once already InProgress.
        var fromState = evaluation.State;
        try
        {
            evaluation.OpenScoring(scope.UserId!.Value);
        }
        catch (DomainException ex)
        {
            return new MyEvaluationResult.InvalidState(ex.Message);
        }
        if (evaluation.State != fromState)
        {
            await auditLogger.LogAsync("Evaluation", evaluation.Id, "evaluation.scoring_started", scope.UserId,
                referenceCode: rfq.ReferenceCode, fromState: fromState.ToString(), toState: evaluation.State.ToString(), ct: ct);
        }
        await db.SaveChangesAsync(ct);

        // T-067: the RFQ's own items and requirements, so an evaluator can read the specification
        // the bids answer. Loaded explicitly because LoadScopedByAssignmentAsync fetches the Rfq bare.
        await db.Entry(rfq).Collection(r => r.Items).LoadAsync(ct);
        await db.Entry(rfq).Collection(r => r.Requirements).LoadAsync(ct);

        var bids = await EvaluationLoader.EvaluatorBidsAsync(db, rfq.Id, ct);
        return new MyEvaluationResult.Success(EvaluationDtoMapper.ToMyDto(evaluation, rfq, scope.UserId!.Value, bids));
    }
}

/// <summary>FEAT-11.3/FR-EVL-003/004/005 - the two-envelope gate's enforcement point, see
/// EvaluationAggregate.ScoreCriterion's own doc comment.</summary>
public sealed class ScoreCriterionHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IScoreCriterionHandler
{
    public async Task<MyEvaluationResult> HandleAsync(ScoreCriterionCommand command, CancellationToken ct)
    {
        var loaded = await EvaluationLoader.LoadScopedByAssignmentAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null) return new MyEvaluationResult.NotFoundOrNotAssigned();
        var (rfq, evaluation) = loaded.Value;

        // T-067: the same projection the read uses, so a bid this evaluator cannot SEE is a bid they
        // cannot SCORE - one source of truth for which proposals are in play.
        await db.Entry(rfq).Collection(r => r.Items).LoadAsync(ct);
        await db.Entry(rfq).Collection(r => r.Requirements).LoadAsync(ct);
        var bids = await EvaluationLoader.EvaluatorBidsAsync(db, rfq.Id, ct);

        // T-068: the public code resolves to a GUID here, at the boundary. An unknown code and a code
        // belonging to a different RFQ are the same miss, and the domain's own validProposalIds guard
        // still runs behind this - two independent refusals rather than one.
        var target = bids.FirstOrDefault(b => b.ProposalCode == command.ProposalCode);
        if (target is null) return new MyEvaluationResult.NotFoundOrNotAssigned();

        var proposalIds = bids.Select(b => b.ProposalId).ToList();
        var existingScoreIds = evaluation.Scores.Select(s => s.Id).ToHashSet();
        try
        {
            evaluation.ScoreCriterion(scope.UserId!.Value, target.ProposalId, command.CriterionId, command.RawScore, command.CommentAr, command.CommentEn, proposalIds.ToHashSet());
        }
        catch (DomainException ex)
        {
            return new MyEvaluationResult.InvalidState(ex.Message);
        }
        // See AssignEvaluatorsHandler's own comment on why this is forced explicitly rather than
        // left to DetectChanges' fixup heuristic.
        foreach (var score in evaluation.Scores.Where(s => !existingScoreIds.Contains(s.Id)))
        {
            db.Entry(score).State = EntityState.Added;
        }

        await auditLogger.LogAsync("Evaluation", evaluation.Id, "evaluation.score", scope.UserId, referenceCode: rfq.ReferenceCode,
            toState: $"{command.ProposalCode}/{command.CriterionId}={command.RawScore}", ct: ct);
        await db.SaveChangesAsync(ct);
        return new MyEvaluationResult.Success(EvaluationDtoMapper.ToMyDto(evaluation, rfq, scope.UserId!.Value, bids));
    }
}

public sealed class SubmitEvaluatorHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : ISubmitEvaluatorHandler
{
    public async Task<MyEvaluationResult> HandleAsync(SubmitEvaluatorCommand command, CancellationToken ct)
    {
        var loaded = await EvaluationLoader.LoadScopedByAssignmentAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null) return new MyEvaluationResult.NotFoundOrNotAssigned();
        var (rfq, evaluation) = loaded.Value;

        await db.Entry(rfq).Collection(r => r.Items).LoadAsync(ct);
        await db.Entry(rfq).Collection(r => r.Requirements).LoadAsync(ct);
        var bids = await EvaluationLoader.EvaluatorBidsAsync(db, rfq.Id, ct);
        var proposalIds = bids.Select(b => b.ProposalId).ToList();
        try
        {
            evaluation.SubmitEvaluator(scope.UserId!.Value, proposalIds.ToHashSet());
        }
        catch (DomainException ex)
        {
            return new MyEvaluationResult.InvalidState(ex.Message);
        }

        // §3.3 "InProgress -> EvaluatorSubmitted | In-app to `procurement_officer` WHEN ALL IN". The
        // condition is part of the rule, not an optimisation: telling the officer to consolidate
        // while two evaluators are still scoring is a false prompt.
        if (evaluation.State == EvaluationState.EvaluatorSubmitted)
        {
            NotificationOutbox.EnqueueMany(db, NotificationTypes.EvaluatorSubmitted,
                // A-7: the owner, who is the one who consolidates.
                await NotificationRecipients.RfqOwnerAsync(db, rfq, ct),
                $"{NotificationTypes.EvaluatorSubmitted}:{evaluation.Id}",
                new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["evaluationId"] = evaluation.Id.ToString() });
        }

        await auditLogger.LogAsync("Evaluation", evaluation.Id, "evaluation_evaluator_submitted", scope.UserId,
            referenceCode: rfq.ReferenceCode, toState: nameof(EvaluationState.EvaluatorSubmitted), ct: ct);
        await db.SaveChangesAsync(ct);
        return new MyEvaluationResult.Success(EvaluationDtoMapper.ToMyDto(evaluation, rfq, scope.UserId!.Value, bids));
    }
}

/// <summary>
/// A-1/BRULE-069: resolves a tie that survived every tie-break rung.
///
/// <para>`evaluation.consolidate` rather than a new permission: this is the same act as producing the
/// ranking - the officer who consolidated is the one who can see the tie and is accountable for the
/// order. A new permission would be a new thing to grant on every deployment for no additional
/// separation.</para>
/// </summary>
public sealed class ResolveEvaluationTieHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger)
    : IResolveEvaluationTieHandler
{
    public async Task<EvaluationMutationResult> HandleAsync(ResolveEvaluationTieCommand command, CancellationToken ct)
    {
        var loaded = await EvaluationLoader.LoadScopedByOrgAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null) return new EvaluationMutationResult.NotFoundOrOutOfScope();
        var (rfq, evaluation) = loaded.Value;

        // The public code resolves to the internal id here, inside the boundary, and only within this
        // RFQ - so a code from another tender cannot address this evaluation's results.
        var proposalId = await db.Proposals
            .Where(p => p.RfqId == rfq.Id && p.ReferenceCode == command.ProposalCode)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);
        if (proposalId is null) return new EvaluationMutationResult.NotFoundOrOutOfScope();

        try
        {
            evaluation.ResolveTie(proposalId.Value, scope.UserId!.Value, command.Reason);
        }
        catch (DomainException ex)
        {
            return new EvaluationMutationResult.InvalidState(ex.Message);
        }

        // The reason IS the record here - a tie broken by a person with no stated basis is exactly what
        // A-1 refuses to let the SYSTEM do, so it must not be what the person does either.
        await auditLogger.LogAsync("Evaluation", evaluation.Id, "evaluation_tie_resolved", scope.UserId,
            referenceCode: rfq.ReferenceCode, reason: command.Reason,
            changes: $"{{\"proposalCode\":\"{command.ProposalCode}\"}}", ct: ct);
        await db.SaveChangesAsync(ct);

        return new EvaluationMutationResult.Success(EvaluationDtoMapper.ToDto(evaluation, rfq));
    }
}

/// <summary>
/// A-8/BRULE-067: the recusal declaration window.
///
/// <para><b>This read does NOT open scoring</b>, which is the whole reason it is a separate endpoint:
/// GET my-evaluation transitions the evaluation to InProgress as a side effect, so an evaluator who
/// loaded the workspace first would have passed the window before ever seeing a name.</para>
/// </summary>
public sealed class GetConflictDeclarationHandler(AppDbContext db, IScopeContext scope) : IGetConflictDeclarationHandler
{
    public async Task<ConflictDeclarationDto?> HandleAsync(string rfqReferenceCode, CancellationToken ct)
    {
        var loaded = await EvaluationLoader.LoadScopedByAssignmentAsync(db, scope, rfqReferenceCode, ct);
        if (loaded is null) return null;
        var (rfq, evaluation) = loaded.Value;

        var assignment = evaluation.Assignments.FirstOrDefault(a => a.EvaluatorUserId == scope.UserId && a.IsActive);
        if (assignment is null) return null;

        // The window is closed once declared. Returning the names anyway would make the anonymity during
        // scoring decorative: an evaluator could re-read this endpoint mid-scoring and look up whose bid
        // they were marking.
        if (assignment.ConflictDeclaredAt is not null)
        {
            return new ConflictDeclarationDto(false, []);
        }

        // Joined and filtered in SQL, then projected and ordered IN MEMORY. Projecting into a record and
        // then ordering by one of its properties is the shape that answered 500 on the reference-data
        // list in batch 9 - it either translates or does not depending on the provider version. A
        // committee's bid list is a handful of rows, so the round trip is the same either way.
        var rows = await db.Proposals.AsNoTracking()
            .Where(p => p.RfqId == rfq.Id && ProposalStates.InEvaluation.Contains(p.State))
            .Join(db.Suppliers.AsNoTracking(), p => p.SupplierId, sup => sup.Id,
                (p, sup) => new { p.ReferenceCode, sup.DisplayNameAr, sup.DisplayNameEn })
            .ToListAsync(ct);

        var bidders = rows
            .OrderBy(r => r.ReferenceCode, StringComparer.Ordinal)
            .Select(r => new DeclarationBidderDto(r.ReferenceCode, r.DisplayNameAr, r.DisplayNameEn))
            .ToList();

        return new ConflictDeclarationDto(true, bidders);
    }
}

/// <summary>
/// A-8/BRULE-067: the declaration itself. A conflict recuses the evaluator with their stated reason -
/// reusing the recusal the domain and the audit trail already have - and no conflict closes the window.
/// </summary>
public sealed class DeclareConflictHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger)
    : IDeclareConflictHandler
{
    public async Task<EvaluationMutationResult> HandleAsync(DeclareConflictCommand command, CancellationToken ct)
    {
        var loaded = await EvaluationLoader.LoadScopedByAssignmentAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null) return new EvaluationMutationResult.NotFoundOrOutOfScope();
        var (rfq, evaluation) = loaded.Value;

        try
        {
            if (command.HasConflict)
            {
                // A self-recusal, and the reason is mandatory for the same purpose it is when a manager
                // recuses someone: an unexplained withdrawal from a committee is not an audit record.
                evaluation.RecuseEvaluator(scope.UserId!.Value, command.Reason ?? string.Empty);
            }
            else
            {
                evaluation.DeclareNoConflict(scope.UserId!.Value);
            }
        }
        catch (DomainException ex)
        {
            return new EvaluationMutationResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("Evaluation", evaluation.Id,
            command.HasConflict ? "evaluator_self_recused" : "evaluator_declared_no_conflict",
            scope.UserId, referenceCode: rfq.ReferenceCode, reason: command.Reason, ct: ct);
        await db.SaveChangesAsync(ct);

        return new EvaluationMutationResult.Success(EvaluationDtoMapper.ToDto(evaluation, rfq));
    }
}
