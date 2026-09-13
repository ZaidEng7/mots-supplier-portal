// The shapes a tender is read through, on both sides.
//
// The items, requirements, attachments, approval steps, invitations and addenda each have their own shape,
// and the tender's own shape carries them.
//
// A requirement's expected envelope tells the supplier which envelope a document answering it belongs in. It
// is advisory: the tag on the file itself is what the system acts on.
//
// The buyer's view of an invitation includes the invited supplier's names, so the status board renders without
// asking the server again.
//
// An invitation candidate is a supplier suggested because its catalogue matches one or more of the tender's
// item categories. The match count ranks them, with more matching categories first.
//
//
// TWO PERSONAS, TWO SHAPES
//
// The buyer's shape and the supplier's shape are separate types rather than one shape with fields left out.
//
// That is deliberate and it is a safety property rather than a style choice: a single shape deciding field by
// field what to include would make a cross-persona leak a run-time branch, whereas today the supplier's shape
// has no member that could carry buyer-only content.
//
// The list shapes are separate for the same reason.

namespace MotsSupplierPortal.Application.Rfqs;

using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Application.Common;

public sealed record RfqItemDto(
    Guid Id, int LineNo, string TitleAr, string TitleEn, string? SpecificationAr, string? SpecificationEn,
    string CategoryCode, decimal Quantity, string UnitOfMeasureCode, bool IsUnitPrice, bool IsOptional);

public sealed record RequirementDto(
    Guid Id, string TextAr, string TextEn, bool IsMandatory, string? DocumentTypeCode,
    MotsSupplierPortal.Domain.Proposals.ProposalDocumentEnvelope? ExpectedEnvelope = null);

public sealed record RfqAttachmentDto(Guid Id, string OriginalFileName, string ContentType, string? Caption, DateTimeOffset UploadedAt);

public sealed record RfqApprovalDto(int StepNo, Guid? ApproverUserId, RfqApprovalDecision? Decision, string? Comment, DateTimeOffset? DecidedAt);

public sealed record InvitationDto(
    Guid Id, Guid SupplierId, string SupplierDisplayNameAr, string SupplierDisplayNameEn,
    InvitationStatus Status, DateTimeOffset InvitedAt, DateTimeOffset? ViewedAt, DateTimeOffset? RespondedAt, string? DeclineReason);

public sealed record InvitationCandidateDto(Guid SupplierId, string DisplayNameAr, string DisplayNameEn, int MatchCount);

public sealed record ClarificationDto(
    Guid Id, Guid AskedBySupplierId, string AskedBySupplierNameAr, string AskedBySupplierNameEn,
    string Question, string? Answer, ClarificationVisibility Visibility, DateTimeOffset AskedAt, DateTimeOffset? AnsweredAt);

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
    uint RowVersion,
    string? SubmissionDeadlineChangeReason = null,
    DateTimeOffset? SubmissionDeadlineChangedAt = null,
    Guid? OwnerUserId = null,
    string? OwnerName = null,
    Guid? AssignedApproverUserId = null,
    string? AssignedApproverName = null);

public sealed record SupplierRfqDto(
    string RfqCode, string TitleAr, string TitleEn, string? DescriptionAr, string? DescriptionEn,
    string CurrencyCode, RfqState State, DateTimeOffset? SubmissionOpensAt, DateTimeOffset? SubmissionDeadline,
    DateTimeOffset? ClarificationDeadlineAt,
    IReadOnlyList<RfqItemDto> Items, IReadOnlyList<RequirementDto> Requirements, IReadOnlyList<RfqAttachmentDto> Attachments,
    InvitationStatus InvitationStatus, IReadOnlyList<SupplierClarificationDto> Clarifications, IReadOnlyList<AddendumDto> Addenda,
    string? SubmissionDeadlineChangeReason = null,
    DateTimeOffset? SubmissionDeadlineChangedAt = null);

public sealed record RfqListItemDto(
    string ReferenceCode, string TitleAr, string TitleEn, RfqState State, DateTimeOffset CreatedAt,
    Guid? OwnerUserId = null, string? OwnerName = null);

public sealed record SupplierRfqListItemDto(
    string RfqCode, string TitleAr, string TitleEn, RfqState State,
    InvitationStatus InvitationStatus, DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt, BuyingOrgDto? BuyingOrg, int ItemsCount, bool HasDraftProposal,
    DateTimeOffset? SubmissionDeadline = null);

public sealed record BuyingOrgDto(string? Code, string Name);

public static class RfqListFilterValues
{
    public static readonly IReadOnlySet<string> OwnerLiterals =
        new HashSet<string>(StringComparer.Ordinal) { "me", "unassigned" };
}

public sealed record RfqAssigneeDto(Guid UserId, string FullName);

public sealed record RfqAssigneesDto(
    IReadOnlyList<RfqAssigneeDto> Owners,
    IReadOnlyList<RfqAssigneeDto> Approvers);
