// What the tender reads and writes are called.
//
// The lists and the detail reads are split by persona: a buyer's handler and a supplier's handler, each
// returning its own shape. One route chooses between them, so the route converges while the handlers do not.
//
//
// THE ONE HANDLER THAT CARRIES ITS OWN PERMISSION CHECK
//
// Extending a deadline is the officer's, and shortening one requires a manager.
//
// That check cannot live on the route, because whether a request is a shortening depends on the tender's
// current deadline, which only the handler has read. So the handler refuses a shortening from a caller who
// lacks the permission for it.

namespace MotsSupplierPortal.Application.Rfqs;

using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Application.Common;

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
