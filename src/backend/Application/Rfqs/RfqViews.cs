using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Application.Rfqs;

public sealed record RfqItemDto(
    Guid Id, int LineNo, string TitleAr, string TitleEn, string? SpecificationAr, string? SpecificationEn,
    string CategoryCode, decimal Quantity, string UnitOfMeasureCode, bool IsUnitPrice, bool IsOptional);

/// <summary>A-2: <paramref name="ExpectedEnvelope"/> tells the supplier which envelope a document
/// answering this requirement belongs in. Advisory - the tag on the FILE is what the system acts on.</summary>
public sealed record RequirementDto(
    Guid Id, string TextAr, string TextEn, bool IsMandatory, string? DocumentTypeCode,
    MotsSupplierPortal.Domain.Proposals.ProposalDocumentEnvelope? ExpectedEnvelope = null);

public sealed record RfqAttachmentDto(Guid Id, string OriginalFileName, string ContentType, string? Caption, DateTimeOffset UploadedAt);

public sealed record RfqApprovalDto(int StepNo, Guid? ApproverUserId, RfqApprovalDecision? Decision, string? Comment, DateTimeOffset? DecidedAt);

/// <summary>Buyer-facing view of an Invitation - includes the invited supplier's display names so
/// the FEAT-08.7 status board can render without a second round trip.</summary>
public sealed record InvitationDto(
    Guid Id, Guid SupplierId, string SupplierDisplayNameAr, string SupplierDisplayNameEn,
    InvitationStatus Status, DateTimeOffset InvitedAt, DateTimeOffset? ViewedAt, DateTimeOffset? RespondedAt, string? DeclineReason);

/// <summary>FEAT-08.2/FR-INV-002: a supplier suggested as an invitation candidate because its
/// Offerings match one or more of the RFQ's item categories. MatchCount ranks the suggestions -
/// more matching categories first.</summary>
public sealed record InvitationCandidateDto(Guid SupplierId, string DisplayNameAr, string DisplayNameEn, int MatchCount);

/// <summary>Buyer-facing view of a Clarification - always carries the real asker (buyer-side audit
/// need), regardless of Visibility. Never served to a supplier - see SupplierClarificationDto for
/// that shape.</summary>
public sealed record ClarificationDto(
    Guid Id, Guid AskedBySupplierId, string AskedBySupplierNameAr, string AskedBySupplierNameEn,
    string Question, string? Answer, ClarificationVisibility Visibility, DateTimeOffset AskedAt, DateTimeOffset? AnsweredAt);

/// <summary>FEAT-10.3/FR-CLR-003: the supplier-facing shape - deliberately carries no asker
/// identity at all, not even for a PublishedToAll item asked by someone else. IsMine is the only
/// signal a supplier gets about authorship, and it is computed server-side from the real
/// AskedBySupplierId against the caller's own SupplierId - never derived from anything sent by the
/// client.</summary>
public sealed record SupplierClarificationDto(
    Guid Id, string Question, string? Answer, ClarificationVisibility Visibility,
    DateTimeOffset AskedAt, DateTimeOffset? AnsweredAt, bool IsMine);

public sealed record AddendumDto(Guid Id, string TitleAr, string TitleEn, string DescriptionAr, string DescriptionEn, DateTimeOffset IssuedAt);

public sealed record RfqDto(
    string ReferenceCode, Guid OrganizationId, string TitleAr, string TitleEn, string? DescriptionAr, string? DescriptionEn,
    string CurrencyCode, RfqState State, DateTimeOffset? PublishAt, DateTimeOffset? SubmissionOpensAt,
    DateTimeOffset? SubmissionClosesAt, DateTimeOffset? ClarificationDeadlineAt, DateTimeOffset? EvaluationTargetDate,
    Guid? EvaluationTemplateId, int? EvaluationTemplateVersion, string? CancelReason,
    IReadOnlyList<RfqItemDto> Items, IReadOnlyList<RequirementDto> Requirements,
    IReadOnlyList<RfqAttachmentDto> Attachments, IReadOnlyList<RfqApprovalDto> Approvals,
    IReadOnlyList<InvitationDto> Invitations, IReadOnlyList<ClarificationDto> Clarifications, IReadOnlyList<AddendumDto> Addenda,
    // §8.1: the version this read saw, emitted as the ETag and sent back as If-Match.
    uint RowVersion,
    // A-6: why the deadline was last moved. Here rather than in the notification payload - BRULE-091's
    // allow-list is identifiers and public codes, and it already refused a DATE on the grounds that a
    // date is content (T-018), so a free-text reason cannot go there either.
    string? SubmissionDeadlineChangeReason = null,
    DateTimeOffset? SubmissionDeadlineChangedAt = null,
    // A-7: who owns this RFQ. The id AND the name, because the screen shows a person and the SPA has
    // to recognise the current user in them - and a name alone cannot be compared to a token's `sub`.
    // Null on an RFQ created before ownership existed, which the screen renders as "Unassigned"
    // rather than as an empty cell.
    Guid? OwnerUserId = null,
    string? OwnerName = null,
    // A-7: the manager the CURRENT review pass is waiting on, null when the pass named nobody.
    Guid? AssignedApproverUserId = null,
    string? AssignedApproverName = null);

/// <summary>FEAT-08.6/FR-INV-006: the supplier-facing shape of an RFQ - deliberately narrower than
/// RfqDto. Excludes Approvals (internal reviewer comments/decisions) and OrganizationId's sibling
/// internal-only fields; a non-invited supplier never sees this shape at all (404, see
/// SupplierRfqEndpoints).</summary>
public sealed record SupplierRfqDto(
    // R-9 rename pass: §12.4 names this rfqCode and the invitation field invitationStatus.
    // titleAr/titleEn stay split - see SupplierDto's note on why a bilingual pair is not a rename.
    string RfqCode, string TitleAr, string TitleEn, string? DescriptionAr, string? DescriptionEn,
    string CurrencyCode, RfqState State, DateTimeOffset? SubmissionOpensAt, DateTimeOffset? SubmissionDeadline,
    DateTimeOffset? ClarificationDeadlineAt,
    IReadOnlyList<RfqItemDto> Items, IReadOnlyList<RequirementDto> Requirements, IReadOnlyList<RfqAttachmentDto> Attachments,
    InvitationStatus InvitationStatus, IReadOnlyList<SupplierClarificationDto> Clarifications, IReadOnlyList<AddendumDto> Addenda,
    // A-6: the supplier sees WHY their deadline moved, on the screen where the deadline itself is. The
    // notification points them here; BRULE-091 keeps the reason out of the payload.
    string? SubmissionDeadlineChangeReason = null,
    DateTimeOffset? SubmissionDeadlineChangedAt = null);

/// <summary>
/// The buyer RFQ list row (T2 Item 2). Deliberately NOT <see cref="RfqDto"/>: the detail DTO is the
/// reason <c>IncludeAll()</c> loaded seven child collections per RFQ to render three scalar
/// columns. Projected in SQL, so nothing is materialised that the list does not show.
/// </summary>
/// <param name="OwnerUserId">A-7: who owns the row. On the LIST as well as the detail, because
/// "which of these are mine" is a question about a list and answering it by opening each RFQ is not
/// an answer. Null means unassigned - a row anyone may claim.</param>
public sealed record RfqListItemDto(
    string ReferenceCode, string TitleAr, string TitleEn, RfqState State, DateTimeOffset CreatedAt,
    Guid? OwnerUserId = null, string? OwnerName = null);

/// <summary>
/// The supplier RFQ list row. <paramref name="MyInvitationStatus"/> is resolved in SQL against the
/// calling supplier rather than by loading the Invitations collection and filtering in memory.
/// </summary>
/// <param name="BuyingOrg">§12.4's <c>buyingOrg { code, name }</c>.</param>
/// <param name="ItemsCount">§12.4's <c>itemsCount</c>. Computed in SQL - see the handler.</param>
/// <param name="HasDraftProposal">§12.4's <c>hasDraftProposal</c>, relative to the calling supplier.</param>
/// <param name="PublishedAt">§12.4's <c>publishedAt</c>, now that the column exists (§12-A/C).</param>
public sealed record SupplierRfqListItemDto(
    string RfqCode, string TitleAr, string TitleEn, RfqState State,
    InvitationStatus InvitationStatus, DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt, BuyingOrgDto? BuyingOrg, int ItemsCount, bool HasDraftProposal,
    /// <summary>
    /// T-054, §12.4's <c>submissionDeadline</c>. Documented on this list and absent from it - so the
    /// one screen where a supplier decides whether to bid could not show the deadline they would be
    /// bidding against.
    ///
    /// <para>Batch 7 shipped this as <c>SubmissionClosesAt</c>, matching the aggregate, on the
    /// stated ground that the rename belonged in R-9's coordinated pass rather than one field
    /// early. This is that pass, and it converges here.</para>
    /// </summary>
    DateTimeOffset? SubmissionDeadline = null);

/// <summary>
/// §12.4's <c>"buyingOrg": { "code": "ORG-HTL-0007", "name": "Cham Palace Hotels" }</c>.
///
/// <para><b>Code is nullable because the schema has no organization short code.</b> Organization
/// carries an Id (GUIDv7), names, contact details and an optional ExternalId - there is no
/// <c>ORG-…</c> public reference anywhere, and minting one is out of this batch's scope. ExternalId
/// is emitted when set, since it is the only externally-meaningful identifier the aggregate has,
/// and null otherwise. Reported as a documented field the schema cannot produce.</para>
/// </summary>
/// <summary>
/// T-055: <paramref name="Code"/> is the organization's own ORG- reference code.
///
/// <para>It used to be <c>ExternalId</c> - the ERP's identifier for the body - which has no writer
/// anywhere in this codebase, so the field §12.4 documents was null in every response the API could
/// produce. An ERP identifier would have been the wrong source regardless: it belongs to another
/// system and is absent until that system says so.</para>
///
/// <para>Still nullable on the DTO, and non-null in practice. A tender always has a buying body; the
/// nullability is the projection's, not the data's.</para>
/// </summary>
public sealed record BuyingOrgDto(string? Code, string Name);

/// <summary>A-7: the buyer RFQ list's <c>?owner=</c> filter. Same three-value shape as the review
/// queue's <c>?assignedTo=</c> - "me", "unassigned", or a specific officer's id - reusing
/// <c>FilterValues.IsAllowedLiteralOrGuid</c> and its refusal, so an unrecognised value is a 422
/// naming the field rather than a silently unfiltered list.</summary>
public static class RfqListFilterValues
{
    public static readonly IReadOnlySet<string> OwnerLiterals =
        new HashSet<string>(StringComparer.Ordinal) { "me", "unassigned" };
}

/// <summary>
/// A-7: one staff member this RFQ can be handed to.
///
/// <para>Id and name only. Nothing else about a colleague is needed to choose between them, and this
/// read is reachable by an officer who holds no staff-administration permission at all - so it
/// deliberately carries none of what <c>StaffAccountDto</c> carries (email, MFA state, lockout).</para>
/// </summary>
public sealed record RfqAssigneeDto(Guid UserId, string FullName);

/// <summary>
/// A-7: who this RFQ may be assigned to, in the two senses it can be.
///
/// <para>Both lists rather than one, because the two questions have different answers and different
/// askers: an officer submitting for review picks an APPROVER, and a manager reassigning picks an
/// OWNER. Serving them from one route keeps the eligibility rule (organization + active + permission)
/// in one place instead of two that can drift.</para>
/// </summary>
public sealed record RfqAssigneesDto(
    IReadOnlyList<RfqAssigneeDto> Owners,
    IReadOnlyList<RfqAssigneeDto> Approvers);
