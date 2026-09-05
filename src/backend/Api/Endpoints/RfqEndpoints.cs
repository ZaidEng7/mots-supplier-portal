using MotsSupplierPortal.Api.Concurrency;
using MotsSupplierPortal.Api.Errors;
using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Infrastructure.Storage;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Api.Endpoints;

public sealed record RfqBasicsRequest(
    string TitleAr, string TitleEn, string? DescriptionAr, string? DescriptionEn, string CurrencyCode,
    DateTimeOffset? PublishAt, DateTimeOffset? SubmissionOpensAt, DateTimeOffset? SubmissionClosesAt,
    DateTimeOffset? ClarificationDeadlineAt, DateTimeOffset? EvaluationTargetDate);

public sealed class RfqBasicsRequestValidator : AbstractValidator<RfqBasicsRequest>
{
    public RfqBasicsRequestValidator()
    {
        RuleFor(x => x.TitleAr).NotEmpty().MaximumLength(300);
        RuleFor(x => x.TitleEn).NotEmpty().MaximumLength(300);
        RuleFor(x => x.CurrencyCode).NotEmpty().MaximumLength(3);
        RuleFor(x => x.SubmissionClosesAt).GreaterThan(x => x.SubmissionOpensAt)
            .When(x => x.SubmissionOpensAt is not null && x.SubmissionClosesAt is not null);
    }
}

// T-018: one field, and it is the new deadline rather than a duration - a duration needs an anchor
// (D-22's problem) and this endpoint would have to pick one.
public sealed record ChangeSubmissionDeadlineRequest(DateTimeOffset SubmissionDeadline);

public sealed class ChangeSubmissionDeadlineRequestValidator : AbstractValidator<ChangeSubmissionDeadlineRequest>
{
    // Deliberately no "must be in the future" rule here: the domain owns that, and duplicating it
    // would give two answers to the same question the day one of them changed.
    public ChangeSubmissionDeadlineRequestValidator() => RuleFor(x => x.SubmissionDeadline).NotEmpty();
}

public sealed record RfqItemRequest(
    string TitleAr, string TitleEn, string? SpecificationAr, string? SpecificationEn,
    string CategoryCode, decimal Quantity, string UnitOfMeasureCode, bool IsUnitPrice, bool IsOptional);

public sealed class RfqItemRequestValidator : AbstractValidator<RfqItemRequest>
{
    public RfqItemRequestValidator()
    {
        RuleFor(x => x.TitleAr).NotEmpty().MaximumLength(300);
        RuleFor(x => x.TitleEn).NotEmpty().MaximumLength(300);
        RuleFor(x => x.CategoryCode).NotEmpty();
        RuleFor(x => x.UnitOfMeasureCode).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public sealed record RequirementRequest(string TextAr, string TextEn, bool IsMandatory, string? DocumentTypeCode);

public sealed class RequirementRequestValidator : AbstractValidator<RequirementRequest>
{
    public RequirementRequestValidator()
    {
        RuleFor(x => x.TextAr).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.TextEn).NotEmpty().MaximumLength(2000);
    }
}

public sealed record BindEvaluationTemplateRequest(Guid EvaluationTemplateId);

public sealed record ReturnForEditsRequest(string Comments);

public sealed class ReturnForEditsRequestValidator : AbstractValidator<ReturnForEditsRequest>
{
    public ReturnForEditsRequestValidator() => RuleFor(x => x.Comments).NotEmpty().MaximumLength(2000);
}

public sealed record CloseSubmissionRequest(string? Reason);

public sealed record CancelRfqRequest(string Reason);

/// <summary>T3-36. §3.1's guard for "Request clarification" is "Reason; targeted supplier(s)".</summary>
public sealed record RequestClarificationTransitionRequest(string Reason);

public sealed class RequestClarificationTransitionRequestValidator : AbstractValidator<RequestClarificationTransitionRequest>
{
    public RequestClarificationTransitionRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
    }
}

public sealed class CancelRfqRequestValidator : AbstractValidator<CancelRfqRequest>
{
    public CancelRfqRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
}

public sealed record InviteSupplierRequest(Guid SupplierId);

public sealed record AnswerClarificationRequest(string Answer, bool Publish);

public sealed class AnswerClarificationRequestValidator : AbstractValidator<AnswerClarificationRequest>
{
    public AnswerClarificationRequestValidator() => RuleFor(x => x.Answer).NotEmpty().MaximumLength(4000);
}

public sealed record IssueAddendumRequest(string TitleAr, string TitleEn, string DescriptionAr, string DescriptionEn);

public sealed class IssueAddendumRequestValidator : AbstractValidator<IssueAddendumRequest>
{
    public IssueAddendumRequestValidator()
    {
        RuleFor(x => x.TitleAr).NotEmpty().MaximumLength(300);
        RuleFor(x => x.TitleEn).NotEmpty().MaximumLength(300);
        RuleFor(x => x.DescriptionAr).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.DescriptionEn).NotEmpty().MaximumLength(4000);
    }
}

/// <summary>FEAT-07.1..07.10/FR-RFQ-001..013. State-transition endpoints are permission-guarded
/// per BUSINESS-PROCESSES.md §3.1's own actor/permission column (verified directly against that
/// table, not inferred) - rfq.publish already existed in the catalog before this session; the rest
/// are new (Permissions.cs's own doc comments explain each).</summary>
public static class RfqEndpoints
{
    private static IResult MapMutation(RfqMutationResult result) => result switch
    {
        RfqMutationResult.Success s => Results.Ok(s.Rfq),
        RfqMutationResult.NotFoundOrOutOfScope => Results.NotFound(),
        // §3: "Illegal transitions return 409 Conflict … listing the current state and the allowed
        // next states." Every RFQ transition answered 400 before T3-36.
        RfqMutationResult.IllegalTransition illegal => IllegalTransitionResult.For(illegal.CurrentState, illegal.Message),
        RfqMutationResult.InvalidState invalid => Results.BadRequest(new { error = "invalid_state", message = invalid.Message }),
        // T-018: a 403, not a 404. The caller demonstrably CAN see this RFQ - they reached here
        // holding rfq.edit on it - so §9.2's hide-existence rule has nothing to protect, and hiding
        // the reason would leave an officer unable to tell "wrong direction" from "broken".
        RfqMutationResult.DeadlineChangeNotPermitted =>
            Results.Json(new { error = "deadline_change_not_permitted" }, statusCode: StatusCodes.Status403Forbidden),

        RfqMutationResult.InvalidCategory => Results.BadRequest(new { error = "invalid_category" }),
        RfqMutationResult.InvalidUnitOfMeasure => Results.BadRequest(new { error = "invalid_unit_of_measure" }),
        RfqMutationResult.InvalidEvaluationTemplate invalid => Results.BadRequest(new { error = "invalid_evaluation_template", message = invalid.Message }),
        RfqMutationResult.SupplierNotActive => Results.BadRequest(new { error = "supplier_not_active" }),
        _ => Results.Problem(),
    };

    /// <summary>Supplier-side result mapping, moved here with the routes it serves. 404 (never
    /// 403) for a non-invited supplier, per §9.2's "avoid leaking existence".</summary>
    private static IResult MapSupplierResult(SupplierRfqResult result) => result switch
    {
        SupplierRfqResult.Success s => Results.Ok(s.Rfq),
        SupplierRfqResult.NotFoundOrNotInvited => Results.NotFound(),
        SupplierRfqResult.InvalidState invalid => Results.BadRequest(new { error = "invalid_state", message = invalid.Message }),
        _ => Results.Problem(),
    };

    public static void MapRfqEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/rfqs").WithTags("Rfqs");

        // §12-A/C1: ONE collection, two personas. §12.4 heads this route "supplier-facing list of
        // invited/published RFQs" while documenting a buyer transition in the same section, and
        // §9.2 scopes by the caller's own supplierId/orgId rather than by path.
        //
        // The route converges; the HANDLERS DO NOT. Each persona keeps its own handler and its own
        // DTO, and the endpoint only chooses between them. That is deliberate: a single handler
        // deciding per-field what to include would make a cross-persona leak a runtime branch,
        // where today it is structurally impossible - SupplierListInvitedRfqsHandler has no code
        // path that can emit a buyer-only field because its DTO has no such member. Convergence
        // was required by the contract; giving up that property was not.
        group.MapGet("/", async (
            string? cursor, int? pageSize, string? withCount, HttpContext httpContext,
            IScopeContext scope,
            IListRfqsHandler buyerHandler,
            ISupplierListInvitedRfqsHandler supplierHandler,
            CancellationToken ct) =>
        {
            // `withCount` binds to `bool?`, so an unparseable value is refused by model binding with
            // a 400 MALFORMED_JSON - the wrong code for an unprocessable filter value on a GET with
            // no body, and one that names no field. Parsed as text so the refusal is the same
            // 422/INVALID_FILTER_VALUE every other filter value in this API earns.
            if (!FilterValues.TryParseBoolFilter(withCount, out _, out var badWithCount))
            {
                return FilterValues.InvalidFilterValue("withCount", badWithCount!);
            }

            var wantsCount = FilterValues.BoolOrFalse(withCount);
            return scope.SupplierId is not null
                ? ListResponse.Ok(httpContext, await supplierHandler.HandleAsync(cursor, pageSize, wantsCount, ct), pageSize)
                : ListResponse.Ok(httpContext, await buyerHandler.HandleAsync(cursor, pageSize, wantsCount, ct), pageSize);
        })
        // rfq.read, not rfq.create: procurement_manager must approve RFQs (BUSINESS-PROCESSES.md
        // §3.1) and holds no authoring permission, so gating a read on create locked the approver
        // out of the list they approve from. Supplier roles hold it too now - §9.2 makes the
        // permission the gate and row-scope the filter, and a supplier reading the RFQs they were
        // invited to is a read of an RFQ.
        .RequirePermission(Permissions.RfqRead)
        // §6.3 gives "-publishedAt" as its worked example of an RFQ list default. It cannot be this
        // list's key: this is the BUYER's list, which is mostly Drafts, and a draft has no
        // PublishedAt - a keyset on a nullable column silently drops every row where it is null.
        // -createdAt is the documented divergence, and is total over the same set.
        .WithListQuery(ListQueryPolicy.Create("-createdAt", ["createdAt"]))
        .WithName("ListRfqs");

        // §12.4, explicitly: *"Fields visible per persona are row-scoped (a supplier never sees
        // other suppliers' proposals or the evaluation internals)"* and *"- for buyers -
        // invitations[]"*. Same dispatch-not-branch reasoning as the list above.
        group.MapGet("/{referenceCode}", async (
            string referenceCode,
            IScopeContext scope,
            IGetRfqHandler buyerHandler,
            ISupplierGetRfqHandler supplierHandler,
            CancellationToken ct) =>
        {
            if (scope.SupplierId is not null)
            {
                return MapSupplierResult(await supplierHandler.HandleAsync(referenceCode, ct));
            }

            var rfq = await buyerHandler.HandleAsync(referenceCode, ct);
            return rfq is null ? Results.NotFound() : Results.Ok(rfq);
        })
        .RequirePermission(Permissions.RfqRead)
        .WithETag()
        .WithName("GetRfq");

        // §3 lists "/rfqs/{rfqCode}/clarifications" as an RFQ sub-resource. The supplier-side POST
        // moves here from /suppliers/me/rfqs/{code}/clarifications; the buyer-side answer/publish
        // routes were already on this collection.
        group.MapPost("/{referenceCode}/clarifications", async (
            string referenceCode,
            PostClarificationRequest request,
            IValidator<PostClarificationRequest> validator,
            ISupplierPostClarificationHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            return MapSupplierResult(await handler.HandleAsync(new PostClarificationQuestionCommand(referenceCode, request.Question), ct));
        })
        .RequirePermission(Permissions.ProposalCreate)
        .WithName("SupplierPostClarification");

        // INVENTION - reported as such. §3 names "/rfqs/{rfqCode}/invitations" as the sub-resource
        // and makes state transitions POSTs on a sub-resource, but names no decline transition
        // anywhere. This composes the two documented rules rather than transcribing a documented
        // path: the invitation is what is being declined, so the transition hangs off it.
        group.MapPost("/{referenceCode}/invitations/decline", async (
            string referenceCode, DeclineInvitationRequest request, ISupplierDeclineInvitationHandler handler, CancellationToken ct) =>
            MapSupplierResult(await handler.HandleAsync(new DeclineInvitationCommand(referenceCode, request.Reason), ct)))
        .RequirePermission(Permissions.ProposalCreate)
        .WithName("SupplierDeclineInvitation");

        group.MapPost("/", async (
            RfqBasicsRequest request,
            IValidator<RfqBasicsRequest> validator,
            ICreateRfqHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.HandleAsync(new CreateRfqCommand(
                request.TitleAr, request.TitleEn, request.DescriptionAr, request.DescriptionEn, request.CurrencyCode,
                request.PublishAt, request.SubmissionOpensAt, request.SubmissionClosesAt,
                request.ClarificationDeadlineAt, request.EvaluationTargetDate), ct);
            return MapMutation(result);
        })
        .RequirePermission(Permissions.RfqCreate)
        .WithName("CreateRfq");

        group.MapPut("/{referenceCode}", async (
            string referenceCode,
            RfqBasicsRequest request,
            IValidator<RfqBasicsRequest> validator,
            IUpdateRfqBasicsHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.HandleAsync(new UpdateRfqBasicsCommand(
                referenceCode, request.TitleAr, request.TitleEn, request.DescriptionAr, request.DescriptionEn,
                request.CurrencyCode, request.PublishAt, request.SubmissionOpensAt, request.SubmissionClosesAt,
                request.ClarificationDeadlineAt, request.EvaluationTargetDate), ct);
            return MapMutation(result);
        })
        .RequirePermission(Permissions.RfqEdit)
        .RequireIfMatch()
        .WithName("UpdateRfqBasics");

        group.MapPost("/{referenceCode}/items", async (
            string referenceCode,
            RfqItemRequest request,
            IValidator<RfqItemRequest> validator,
            IManageRfqItemHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.AddAsync(new AddRfqItemCommand(
                referenceCode, request.TitleAr, request.TitleEn, request.SpecificationAr, request.SpecificationEn,
                request.CategoryCode, request.Quantity, request.UnitOfMeasureCode, request.IsUnitPrice, request.IsOptional), ct);
            return MapMutation(result);
        })
        .RequirePermission(Permissions.RfqEdit)
        .WithName("AddRfqItem");

        group.MapDelete("/{referenceCode}/items/{itemId:guid}", async (
            string referenceCode, Guid itemId, IManageRfqItemHandler handler, CancellationToken ct) =>
            MapMutation(await handler.RemoveAsync(new RemoveRfqItemCommand(referenceCode, itemId), ct)))
        .RequirePermission(Permissions.RfqEdit)
        .WithName("RemoveRfqItem");

        group.MapPost("/{referenceCode}/requirements", async (
            string referenceCode,
            RequirementRequest request,
            IValidator<RequirementRequest> validator,
            IManageRequirementHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.AddAsync(new AddRequirementCommand(
                referenceCode, request.TextAr, request.TextEn, request.IsMandatory, request.DocumentTypeCode), ct);
            return MapMutation(result);
        })
        .RequirePermission(Permissions.RfqEdit)
        .WithName("AddRequirement");

        group.MapDelete("/{referenceCode}/requirements/{requirementId:guid}", async (
            string referenceCode, Guid requirementId, IManageRequirementHandler handler, CancellationToken ct) =>
            MapMutation(await handler.RemoveAsync(new RemoveRequirementCommand(referenceCode, requirementId), ct)))
        .RequirePermission(Permissions.RfqEdit)
        .WithName("RemoveRequirement");

        // FEAT-07.2/FR-RFQ-003. Stored via IFileStorage directly (no AV-scan quarantine flow -
        // that pipeline is SupplierDocument-specific; OQ-014 already tags AV scanning generally
        // as [REQUIRES BUSINESS CONFIRMATION], so this is a real, deliberate scope decision for
        // this session, not a silently-skipped security step).
        group.MapPost("/{referenceCode}/attachments", async (
            string referenceCode,
            HttpRequest request,
            IFileStorage fileStorage,
            IManageRfqAttachmentHandler handler,
            CancellationToken ct) =>
        {
            if (!request.HasFormContentType) return Results.BadRequest(new { error = "expected_multipart_form" });

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0) return Results.BadRequest(new { error = "file_required" });

            var caption = form["caption"].ToString();
            // Server-side key, and the quarantine gap both these paths share, are explained once in
            // AttachmentStorageKey rather than twice here.
            var storageKey = AttachmentStorageKey.For(AttachmentStorageKey.RfqAttachmentPrefix);

            await using (var stream = file.OpenReadStream())
            {
                await fileStorage.SaveAsync(storageKey, stream, file.ContentType, ct);
            }

            var result = await handler.AddAsync(new AddRfqAttachmentCommand(
                referenceCode, storageKey, file.FileName, file.ContentType, string.IsNullOrWhiteSpace(caption) ? null : caption), ct);
            return MapMutation(result);
        })
        .RequirePermission(Permissions.RfqEdit)
        .WithName("AddRfqAttachment");

        // T3-01: the read path FEAT-07.2 never had. Upload and delete existed; a buyer could attach
        // the specification an invited supplier is meant to bid against, and that supplier could
        // never open it.
        //
        // Gated on rfq.read rather than rfq.edit: an invited SUPPLIER must reach this, and they hold
        // no editing permission on a buyer's RFQ. Row scope is the handler's - it is "your
        // organization's RFQ" for staff and "an RFQ you were invited to, once published" for a
        // supplier, and neither is expressible as a declarative policy.
        group.MapGet("/{referenceCode}/attachments/{attachmentId:guid}/download-url", async (
            string referenceCode,
            Guid attachmentId,
            IGetRfqAttachmentDownloadUrlHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(referenceCode, attachmentId, ct);
            return result switch
            {
                RfqAttachmentDownloadResult.Success s => Results.Ok(new { url = s.Url, fileName = s.FileName }),
                _ => Results.NotFound(),
            };
        })
        .RequirePermission(Permissions.RfqRead)
        .WithName("GetRfqAttachmentDownloadUrl");

        group.MapDelete("/{referenceCode}/attachments/{attachmentId:guid}", async (
            string referenceCode, Guid attachmentId, IManageRfqAttachmentHandler handler, CancellationToken ct) =>
            MapMutation(await handler.RemoveAsync(new RemoveRfqAttachmentCommand(referenceCode, attachmentId), ct)))
        .RequirePermission(Permissions.RfqEdit)
        .WithName("RemoveRfqAttachment");

        group.MapPut("/{referenceCode}/evaluation-template", async (
            string referenceCode, BindEvaluationTemplateRequest request, IBindEvaluationTemplateHandler handler, CancellationToken ct) =>
            MapMutation(await handler.HandleAsync(new BindEvaluationTemplateCommand(referenceCode, request.EvaluationTemplateId), ct)))
        .RequirePermission(Permissions.RfqEdit)
        .RequireIfMatch()
        .WithName("BindEvaluationTemplate");

        // T-018/BRULE-035. NO permission filter on the route, deliberately, and this was got wrong
        // first: BRULE-035 gives extension to the officer (rfq.edit) and shortening to the manager
        // (rfq.deadline.shorten), and procurement_manager does NOT hold rfq.edit. A route requiring
        // rfq.edit therefore 403'd the manager before the handler ran - the very caller the rule names
        // for shortening. There is no "any of these permissions" filter in this codebase, and adding
        // one to express a rule that is really "it depends on the direction" would be the wrong shape.
        // Both checks live in the handler, which is the only place the direction is known.
        group.MapPost("/{referenceCode}/deadline", async (
            string referenceCode, ChangeSubmissionDeadlineRequest request,
            IValidator<ChangeSubmissionDeadlineRequest> validator,
            IChangeSubmissionDeadlineHandler handler, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            return MapMutation(await handler.HandleAsync(
                new ChangeSubmissionDeadlineCommand(referenceCode, request.SubmissionDeadline), ct));
        })
        .RequireAuthorization()
        .RequireIfMatch()
        .WithETag()
        .WithName("ChangeSubmissionDeadline");

        group.MapPost("/{referenceCode}/submit-review", async (
            string referenceCode, ISubmitRfqForReviewHandler handler, CancellationToken ct) =>
            MapMutation(await handler.HandleAsync(new SubmitRfqForReviewCommand(referenceCode), ct)))
        .RequirePermission(Permissions.RfqSubmitReview)
        .RequireIfMatch()
        .WithName("SubmitRfqForReview");

        group.MapPost("/{referenceCode}/return", async (
            string referenceCode,
            ReturnForEditsRequest request,
            IValidator<ReturnForEditsRequest> validator,
            IReturnRfqForEditsHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            return MapMutation(await handler.HandleAsync(new ReturnRfqForEditsCommand(referenceCode, request.Comments), ct));
        })
        .RequirePermission(Permissions.RfqReview)
        .RequireIfMatch()
        .WithName("ReturnRfqForEdits");

        group.MapPost("/{referenceCode}/approve", async (
            string referenceCode, IApproveRfqHandler handler, CancellationToken ct) =>
            MapMutation(await handler.HandleAsync(new ApproveRfqCommand(referenceCode), ct)))
        .RequirePermission(Permissions.RfqApprove)
        .RequireIfMatch()
        .WithName("ApproveRfq");

        group.MapPost("/{referenceCode}/publish", async (
            string referenceCode, IPublishRfqHandler handler, CancellationToken ct) =>
            MapMutation(await handler.HandleAsync(new PublishRfqCommand(referenceCode), ct)))
        .RequirePermission(Permissions.RfqPublish)
        .RequireIfMatch()
        .WithName("PublishRfq");

        group.MapPost("/{referenceCode}/close", async (
            string referenceCode, CloseSubmissionRequest request, ICloseRfqSubmissionHandler handler, CancellationToken ct) =>
            MapMutation(await handler.HandleAsync(new CloseRfqSubmissionCommand(referenceCode, request.Reason), ct)))
        .RequirePermission(Permissions.RfqClose)
        .RequireIfMatch()
        .WithName("CloseRfqSubmission");

        // T3-36. §3.1's two clarification transitions. Named POST sub-resources, per §3's rule of
        // thumb: "if an operation moves an aggregate through its state machine, it is a named
        // transition endpoint". Distinct paths from /clarifications, which is the supplier Q&A.
        group.MapPost("/{referenceCode}/request-clarification", async (
            string referenceCode, RequestClarificationTransitionRequest request,
            IValidator<RequestClarificationTransitionRequest> validator,
            IRequestRfqClarificationHandler handler, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            return MapMutation(await handler.HandleAsync(new RequestRfqClarificationCommand(referenceCode, request.Reason), ct));
        })
        .RequirePermission(Permissions.RfqClarify)
        // NO RequireIfMatch, and this is a deliberate exception to §8.1's "transition POST" rule
        // rather than an oversight. §3.1 names `evaluator` as an actor for this transition, and an
        // evaluator holds neither `rfq.read` nor, necessarily, an OrganizationId - so they cannot
        // GET the RFQ, cannot obtain its ETag, and would be answered 428 on a route the process
        // document says is theirs. §8.1's guard exists to stop lost updates; neither of these
        // transitions carries state that another writer could overwrite. Reported as the permission
        // gap it is rather than closed by widening what an evaluator can read.
        .WithName("RequestRfqClarification");

        group.MapPost("/{referenceCode}/resolve-clarification", async (
            string referenceCode, IResolveRfqClarificationHandler handler, CancellationToken ct) =>
            MapMutation(await handler.HandleAsync(new ResolveRfqClarificationCommand(referenceCode), ct)))
        .RequirePermission(Permissions.RfqClarify)
        // Same exception, same reason - see RequestRfqClarification above.
        .WithName("ResolveRfqClarification");

        group.MapPost("/{referenceCode}/cancel", async (
            string referenceCode,
            CancelRfqRequest request,
            IValidator<CancelRfqRequest> validator,
            ICancelRfqHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            return MapMutation(await handler.HandleAsync(new CancelRfqCommand(referenceCode, request.Reason), ct));
        })
        .RequirePermission(Permissions.RfqCancel)
        .RequireIfMatch()
        .WithName("CancelRfq");

        group.MapPost("/{referenceCode}/invitations", async (
            string referenceCode, InviteSupplierRequest request, IInviteSupplierHandler handler, CancellationToken ct) =>
            MapMutation(await handler.HandleAsync(new InviteSupplierCommand(referenceCode, request.SupplierId), ct)))
        .RequirePermission(Permissions.RfqInvite)
        .WithName("InviteSupplier");

        group.MapGet("/{referenceCode}/invitations/candidates", async (
            string referenceCode, ISuggestInvitationCandidatesHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(referenceCode, ct)))
        .RequirePermission(Permissions.RfqInvite)
        .WithName("SuggestInvitationCandidates");

        group.MapPost("/{referenceCode}/clarifications/{clarificationId:guid}/answer", async (
            string referenceCode,
            Guid clarificationId,
            AnswerClarificationRequest request,
            IValidator<AnswerClarificationRequest> validator,
            IAnswerClarificationHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            return MapMutation(await handler.HandleAsync(new AnswerClarificationCommand(referenceCode, clarificationId, request.Answer, request.Publish), ct));
        })
        .RequirePermission(Permissions.ClarificationAnswer)
        .WithName("AnswerClarification");

        group.MapPost("/{referenceCode}/clarifications/{clarificationId:guid}/publish", async (
            string referenceCode, Guid clarificationId, IPublishClarificationHandler handler, CancellationToken ct) =>
            MapMutation(await handler.HandleAsync(new PublishClarificationCommand(referenceCode, clarificationId), ct)))
        .RequirePermission(Permissions.ClarificationAnswer)
        .WithName("PublishClarification");

        group.MapPost("/{referenceCode}/addenda", async (
            string referenceCode,
            IssueAddendumRequest request,
            IValidator<IssueAddendumRequest> validator,
            IIssueAddendumHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            return MapMutation(await handler.HandleAsync(new IssueAddendumCommand(referenceCode, request.TitleAr, request.TitleEn, request.DescriptionAr, request.DescriptionEn), ct));
        })
        .RequirePermission(Permissions.RfqAddendum)
        .WithName("IssueAddendum");
    }
}
