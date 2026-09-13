using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Application.Rfqs;

public interface IListRfqsHandler
{
    Task<ListEnvelope<RfqListItemDto>> HandleAsync(string? cursor, int? pageSize, bool withCount, string? owner, CancellationToken ct);
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

/// <summary>
/// T-018/BRULE-035. Extension is the officer's; shortening "requires procurement_manager", so the
/// handler refuses a shortening from a caller without <c>rfq.deadline.shorten</c>. That check cannot
/// live on the route: whether a request is a shortening depends on the RFQ's current deadline, which
/// only the handler has read.
/// </summary>
public interface IChangeSubmissionDeadlineHandler
{
    Task<RfqMutationResult> HandleAsync(ChangeSubmissionDeadlineCommand command, CancellationToken ct);
}

public interface IManageRfqItemHandler
{
    Task<RfqMutationResult> AddAsync(AddRfqItemCommand command, CancellationToken ct);
    Task<RfqMutationResult> UpdateAsync(UpdateRfqItemCommand command, CancellationToken ct);
    Task<RfqMutationResult> RemoveAsync(RemoveRfqItemCommand command, CancellationToken ct);
}

public interface IManageRequirementHandler
{
    Task<RfqMutationResult> AddAsync(AddRequirementCommand command, CancellationToken ct);
    Task<RfqMutationResult> UpdateAsync(UpdateRequirementCommand command, CancellationToken ct);
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

public interface IListRfqAssigneesHandler
{
    Task<RfqAssigneesDto?> HandleAsync(string referenceCode, CancellationToken ct);
}

public interface IReassignRfqHandler
{
    Task<RfqMutationResult> HandleAsync(ReassignRfqCommand command, CancellationToken ct);
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

public interface IRequestRfqClarificationHandler
{
    Task<RfqMutationResult> HandleAsync(RequestRfqClarificationCommand command, CancellationToken ct);
}

public interface IResolveRfqClarificationHandler
{
    Task<RfqMutationResult> HandleAsync(ResolveRfqClarificationCommand command, CancellationToken ct);
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
