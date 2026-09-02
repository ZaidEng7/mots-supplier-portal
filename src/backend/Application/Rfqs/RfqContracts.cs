using MotsSupplierPortal.Domain.Rfqs;

using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Application.Rfqs;

public sealed record RfqItemDto(
    Guid Id, int LineNo, string TitleAr, string TitleEn, string? SpecificationAr, string? SpecificationEn,
    string CategoryCode, decimal Quantity, string UnitOfMeasureCode, bool IsUnitPrice, bool IsOptional);

public sealed record RequirementDto(Guid Id, string TextAr, string TextEn, bool IsMandatory, string? DocumentTypeCode);

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
    IReadOnlyList<InvitationDto> Invitations, IReadOnlyList<ClarificationDto> Clarifications, IReadOnlyList<AddendumDto> Addenda);

/// <summary>FEAT-08.6/FR-INV-006: the supplier-facing shape of an RFQ - deliberately narrower than
/// RfqDto. Excludes Approvals (internal reviewer comments/decisions) and OrganizationId's sibling
/// internal-only fields; a non-invited supplier never sees this shape at all (404, see
/// SupplierRfqEndpoints).</summary>
public sealed record SupplierRfqDto(
    string ReferenceCode, string TitleAr, string TitleEn, string? DescriptionAr, string? DescriptionEn,
    string CurrencyCode, RfqState State, DateTimeOffset? SubmissionOpensAt, DateTimeOffset? SubmissionClosesAt,
    DateTimeOffset? ClarificationDeadlineAt,
    IReadOnlyList<RfqItemDto> Items, IReadOnlyList<RequirementDto> Requirements, IReadOnlyList<RfqAttachmentDto> Attachments,
    InvitationStatus MyInvitationStatus, IReadOnlyList<SupplierClarificationDto> Clarifications, IReadOnlyList<AddendumDto> Addenda);

/// <summary>
/// The buyer RFQ list row (T2 Item 2). Deliberately NOT <see cref="RfqDto"/>: the detail DTO is the
/// reason <c>IncludeAll()</c> loaded seven child collections per RFQ to render three scalar
/// columns. Projected in SQL, so nothing is materialised that the list does not show.
/// </summary>
public sealed record RfqListItemDto(
    string ReferenceCode, string TitleAr, string TitleEn, RfqState State, DateTimeOffset CreatedAt);

/// <summary>
/// The supplier RFQ list row. <paramref name="MyInvitationStatus"/> is resolved in SQL against the
/// calling supplier rather than by loading the Invitations collection and filtering in memory.
/// </summary>
/// <param name="BuyingOrg">§12.4's <c>buyingOrg { code, name }</c>.</param>
/// <param name="ItemsCount">§12.4's <c>itemsCount</c>. Computed in SQL - see the handler.</param>
/// <param name="HasDraftProposal">§12.4's <c>hasDraftProposal</c>, relative to the calling supplier.</param>
/// <param name="PublishedAt">§12.4's <c>publishedAt</c>, now that the column exists (§12-A/C).</param>
public sealed record SupplierRfqListItemDto(
    string ReferenceCode, string TitleAr, string TitleEn, RfqState State,
    InvitationStatus MyInvitationStatus, DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt, BuyingOrgDto? BuyingOrg, int ItemsCount, bool HasDraftProposal);

/// <summary>
/// §12.4's <c>"buyingOrg": { "code": "ORG-HTL-0007", "name": "Cham Palace Hotels" }</c>.
///
/// <para><b>Code is nullable because the schema has no organization short code.</b> Organization
/// carries an Id (GUIDv7), names, contact details and an optional ExternalId - there is no
/// <c>ORG-…</c> public reference anywhere, and minting one is out of this batch's scope. ExternalId
/// is emitted when set, since it is the only externally-meaningful identifier the aggregate has,
/// and null otherwise. Reported as a documented field the schema cannot produce.</para>
/// </summary>
public sealed record BuyingOrgDto(string? Code, string Name);

public sealed record CreateRfqCommand(
    string TitleAr, string TitleEn, string? DescriptionAr, string? DescriptionEn, string CurrencyCode,
    DateTimeOffset? PublishAt, DateTimeOffset? SubmissionOpensAt, DateTimeOffset? SubmissionClosesAt,
    DateTimeOffset? ClarificationDeadlineAt, DateTimeOffset? EvaluationTargetDate);

public sealed record UpdateRfqBasicsCommand(
    string ReferenceCode, string TitleAr, string TitleEn, string? DescriptionAr, string? DescriptionEn, string CurrencyCode,
    DateTimeOffset? PublishAt, DateTimeOffset? SubmissionOpensAt, DateTimeOffset? SubmissionClosesAt,
    DateTimeOffset? ClarificationDeadlineAt, DateTimeOffset? EvaluationTargetDate);

public sealed record AddRfqItemCommand(
    string ReferenceCode, string TitleAr, string TitleEn, string? SpecificationAr, string? SpecificationEn,
    string CategoryCode, decimal Quantity, string UnitOfMeasureCode, bool IsUnitPrice, bool IsOptional);

public sealed record RemoveRfqItemCommand(string ReferenceCode, Guid ItemId);

public sealed record AddRequirementCommand(string ReferenceCode, string TextAr, string TextEn, bool IsMandatory, string? DocumentTypeCode);

public sealed record RemoveRequirementCommand(string ReferenceCode, Guid RequirementId);

public sealed record AddRfqAttachmentCommand(string ReferenceCode, string StorageKey, string OriginalFileName, string ContentType, string? Caption);

public sealed record RemoveRfqAttachmentCommand(string ReferenceCode, Guid AttachmentId);

public sealed record BindEvaluationTemplateCommand(string ReferenceCode, Guid EvaluationTemplateId);

public sealed record SubmitRfqForReviewCommand(string ReferenceCode);

public sealed record ReturnRfqForEditsCommand(string ReferenceCode, string Comments);

public sealed record ApproveRfqCommand(string ReferenceCode);

public sealed record PublishRfqCommand(string ReferenceCode);

public sealed record CloseRfqSubmissionCommand(string ReferenceCode, string? Reason);

public sealed record CancelRfqCommand(string ReferenceCode, string Reason);

public sealed record InviteSupplierCommand(string ReferenceCode, Guid SupplierId);

public sealed record DeclineInvitationCommand(string ReferenceCode, string? Reason);

public sealed record PostClarificationQuestionCommand(string ReferenceCode, string Question);

public sealed record AnswerClarificationCommand(string ReferenceCode, Guid ClarificationId, string Answer, bool Publish);

public sealed record PublishClarificationCommand(string ReferenceCode, Guid ClarificationId);

public sealed record IssueAddendumCommand(string ReferenceCode, string TitleAr, string TitleEn, string DescriptionAr, string DescriptionEn);

public abstract record RfqMutationResult
{
    public sealed record Success(RfqDto Rfq) : RfqMutationResult;
    public sealed record NotFoundOrOutOfScope : RfqMutationResult;
    /// <summary>Wraps every Rfq domain-invariant refusal (illegal transition, missing item,
    /// timeline inconsistency, unbound template, etc.) with the exact DomainException message -
    /// same pattern as ProfileMutationResult.InvalidState/EvaluationTemplateMutationResult.InvalidState.</summary>
    public sealed record InvalidState(string Message) : RfqMutationResult;
    public sealed record InvalidCategory : RfqMutationResult;
    public sealed record InvalidUnitOfMeasure : RfqMutationResult;
    public sealed record InvalidEvaluationTemplate(string Message) : RfqMutationResult;
    /// <summary>FEAT-08.1/BRULE-032: the invited (or, on Publish, already-invited) supplier is not
    /// found or not Active.</summary>
    public sealed record SupplierNotActive : RfqMutationResult;
}

/// <summary>Supplier-side self-service result - deliberately does not reuse RfqMutationResult so a
/// supplier-facing 404 can never be confused with the buyer-side NotFoundOrOutOfScope, which is
/// organization-scoped rather than invitation-scoped.</summary>
public abstract record SupplierRfqResult
{
    public sealed record Success(SupplierRfqDto Rfq) : SupplierRfqResult;
    /// <summary>FEAT-08.6/FR-INV-006: the RFQ does not exist, is not yet Published, or this
    /// supplier holds no Invitation to it - all three collapse to the same 404 at the endpoint so a
    /// non-invited supplier cannot distinguish "wrong reference code" from "not invited".</summary>
    public sealed record NotFoundOrNotInvited : SupplierRfqResult;
    public sealed record InvalidState(string Message) : SupplierRfqResult;
}

public interface IListRfqsHandler
{
    Task<ListEnvelope<RfqListItemDto>> HandleAsync(string? cursor, int? pageSize, bool withCount, CancellationToken ct);
}

public interface IGetRfqHandler
{
    Task<RfqDto?> HandleAsync(string referenceCode, CancellationToken ct);
}

public interface ICreateRfqHandler
{
    Task<RfqMutationResult> HandleAsync(CreateRfqCommand command, CancellationToken ct);
}

public interface IUpdateRfqBasicsHandler
{
    Task<RfqMutationResult> HandleAsync(UpdateRfqBasicsCommand command, CancellationToken ct);
}

public interface IManageRfqItemHandler
{
    Task<RfqMutationResult> AddAsync(AddRfqItemCommand command, CancellationToken ct);
    Task<RfqMutationResult> RemoveAsync(RemoveRfqItemCommand command, CancellationToken ct);
}

public interface IManageRequirementHandler
{
    Task<RfqMutationResult> AddAsync(AddRequirementCommand command, CancellationToken ct);
    Task<RfqMutationResult> RemoveAsync(RemoveRequirementCommand command, CancellationToken ct);
}

public interface IManageRfqAttachmentHandler
{
    Task<RfqMutationResult> AddAsync(AddRfqAttachmentCommand command, CancellationToken ct);
    Task<RfqMutationResult> RemoveAsync(RemoveRfqAttachmentCommand command, CancellationToken ct);
}

public interface IBindEvaluationTemplateHandler
{
    Task<RfqMutationResult> HandleAsync(BindEvaluationTemplateCommand command, CancellationToken ct);
}

public interface ISubmitRfqForReviewHandler
{
    Task<RfqMutationResult> HandleAsync(SubmitRfqForReviewCommand command, CancellationToken ct);
}

public interface IReturnRfqForEditsHandler
{
    Task<RfqMutationResult> HandleAsync(ReturnRfqForEditsCommand command, CancellationToken ct);
}

public interface IApproveRfqHandler
{
    Task<RfqMutationResult> HandleAsync(ApproveRfqCommand command, CancellationToken ct);
}

public interface IPublishRfqHandler
{
    Task<RfqMutationResult> HandleAsync(PublishRfqCommand command, CancellationToken ct);
}

public interface ICloseRfqSubmissionHandler
{
    Task<RfqMutationResult> HandleAsync(CloseRfqSubmissionCommand command, CancellationToken ct);
}

public interface ICancelRfqHandler
{
    Task<RfqMutationResult> HandleAsync(CancelRfqCommand command, CancellationToken ct);
}

public interface IInviteSupplierHandler
{
    Task<RfqMutationResult> HandleAsync(InviteSupplierCommand command, CancellationToken ct);
}

public interface ISuggestInvitationCandidatesHandler
{
    Task<IReadOnlyList<InvitationCandidateDto>> HandleAsync(string referenceCode, CancellationToken ct);
}

public interface ISupplierListInvitedRfqsHandler
{
    Task<ListEnvelope<SupplierRfqListItemDto>> HandleAsync(string? cursor, int? pageSize, bool withCount, CancellationToken ct);
}

public interface ISupplierGetRfqHandler
{
    Task<SupplierRfqResult> HandleAsync(string referenceCode, CancellationToken ct);
}

public interface ISupplierDeclineInvitationHandler
{
    Task<SupplierRfqResult> HandleAsync(DeclineInvitationCommand command, CancellationToken ct);
}

public interface IAnswerClarificationHandler
{
    Task<RfqMutationResult> HandleAsync(AnswerClarificationCommand command, CancellationToken ct);
}

public interface IPublishClarificationHandler
{
    Task<RfqMutationResult> HandleAsync(PublishClarificationCommand command, CancellationToken ct);
}

public interface IIssueAddendumHandler
{
    Task<RfqMutationResult> HandleAsync(IssueAddendumCommand command, CancellationToken ct);
}

public interface ISupplierPostClarificationHandler
{
    Task<SupplierRfqResult> HandleAsync(PostClarificationQuestionCommand command, CancellationToken ct);
}
