// What can be asked of a tender: create it, edit its basics, manage its items, requirements and attachments,
// bind an evaluation template, invite suppliers, answer clarifications, issue addenda, and move it through its
// lifecycle.
//
// Moving a deadline is one command for both directions. Which direction a request is depends on the tender's
// current deadline, so whether the caller is allowed to make that move is a question only the handler can
// answer. The reason is mandatory and travels into both the audit row and the notification.
//
// Correcting an item is the same shape as adding one, plus the line's identifier. A correction is the same
// values in a different state rather than a different operation.

namespace MotsSupplierPortal.Application.Rfqs;

using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Application.Common;

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

public sealed record SubmitRfqForReviewCommand(string ReferenceCode, Guid? AssignedApproverUserId = null);

public sealed record ReassignRfqCommand(string ReferenceCode, Guid NewOwnerUserId, string Reason);

public sealed record ReturnRfqForEditsCommand(string ReferenceCode, string Comments);

public sealed record ApproveRfqCommand(string ReferenceCode);

public sealed record PublishRfqCommand(string ReferenceCode);

public sealed record CloseRfqSubmissionCommand(string ReferenceCode, string? Reason);

public sealed record CancelRfqCommand(string ReferenceCode, string Reason);

public sealed record RequestRfqClarificationCommand(string ReferenceCode, string Reason);

public sealed record ResolveRfqClarificationCommand(string ReferenceCode);

public sealed record InviteSupplierCommand(string ReferenceCode, Guid SupplierId);

public sealed record DeclineInvitationCommand(string ReferenceCode, string? Reason);

public sealed record PostClarificationQuestionCommand(string ReferenceCode, string Question);

public sealed record AnswerClarificationCommand(string ReferenceCode, Guid ClarificationId, string Answer);

public sealed record PublishClarificationCommand(string ReferenceCode, Guid ClarificationId);

public sealed record IssueAddendumCommand(string ReferenceCode, string TitleAr, string TitleEn, string DescriptionAr, string DescriptionEn);
