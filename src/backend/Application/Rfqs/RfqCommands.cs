using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Application.Rfqs;

// T-018/BRULE-035: one command for both directions - see Rfq.ChangeSubmissionDeadline on why the
// direction is an access-control question rather than a domain one.
/// <summary>A-6: the reason is mandatory and is carried into the audit row and the notification.</summary>
public sealed record ChangeSubmissionDeadlineCommand(string ReferenceCode, DateTimeOffset NewCloseAt, string Reason);

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

/// <summary>Corrects a line already on the tender. Same shape as the add, plus the line's id - a
/// correction is the same values in a different state, not a different operation.</summary>
public sealed record UpdateRfqItemCommand(
    string ReferenceCode, Guid ItemId, string TitleAr, string TitleEn, string? SpecificationAr, string? SpecificationEn,
    string CategoryCode, decimal Quantity, string UnitOfMeasureCode, bool IsUnitPrice, bool IsOptional);

public sealed record AddRequirementCommand(
    string ReferenceCode, string TextAr, string TextEn, bool IsMandatory, string? DocumentTypeCode,
    MotsSupplierPortal.Domain.Proposals.ProposalDocumentEnvelope? ExpectedEnvelope = null);

public sealed record RemoveRequirementCommand(string ReferenceCode, Guid RequirementId);

public sealed record UpdateRequirementCommand(
    string ReferenceCode, Guid RequirementId, string TextAr, string TextEn, bool IsMandatory, string? DocumentTypeCode,
    MotsSupplierPortal.Domain.Proposals.ProposalDocumentEnvelope? ExpectedEnvelope = null);

public sealed record AddRfqAttachmentCommand(string ReferenceCode, string StorageKey, string OriginalFileName, string ContentType, string? Caption);

public sealed record RemoveRfqAttachmentCommand(string ReferenceCode, Guid AttachmentId);

public sealed record BindEvaluationTemplateCommand(string ReferenceCode, Guid EvaluationTemplateId);

/// <param name="AssignedApproverUserId">A-7: the manager this pass is waiting on. Optional - see
/// <c>Rfq.SubmitForReview</c> on why an un-nominated pass is a recorded absence rather than a
/// missing default.</param>
public sealed record SubmitRfqForReviewCommand(string ReferenceCode, Guid? AssignedApproverUserId = null);

/// <summary>A-7: hand an RFQ to another officer. The reason is mandatory - the audit row is the whole
/// point of this operation, and a row saying only that ownership moved answers nothing.</summary>
public sealed record ReassignRfqCommand(string ReferenceCode, Guid NewOwnerUserId, string Reason);

public sealed record ReturnRfqForEditsCommand(string ReferenceCode, string Comments);

public sealed record ApproveRfqCommand(string ReferenceCode);

public sealed record PublishRfqCommand(string ReferenceCode);

public sealed record CloseRfqSubmissionCommand(string ReferenceCode, string? Reason);

public sealed record CancelRfqCommand(string ReferenceCode, string Reason);

/// <summary>
/// T3-36. BUSINESS-PROCESSES.md §3.1's "Request clarification" - the EVALUATION-phase pause, not the
/// submission-window Q&amp;A. The reason is the guard the table names.
/// </summary>
public sealed record RequestRfqClarificationCommand(string ReferenceCode, string Reason);

/// <summary>§3.1's "Clarification resolved".</summary>
public sealed record ResolveRfqClarificationCommand(string ReferenceCode);

public sealed record InviteSupplierCommand(string ReferenceCode, Guid SupplierId);

public sealed record DeclineInvitationCommand(string ReferenceCode, string? Reason);

public sealed record PostClarificationQuestionCommand(string ReferenceCode, string Question);

/// <summary>A-4: no publish flag. Answering IS publishing - see Rfq.AnswerClarification.</summary>
public sealed record AnswerClarificationCommand(string ReferenceCode, Guid ClarificationId, string Answer);

public sealed record PublishClarificationCommand(string ReferenceCode, Guid ClarificationId);

public sealed record IssueAddendumCommand(string ReferenceCode, string TitleAr, string TitleEn, string DescriptionAr, string DescriptionEn);
