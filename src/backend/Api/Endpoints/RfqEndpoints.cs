using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
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
        RfqMutationResult.InvalidState invalid => Results.BadRequest(new { error = "invalid_state", message = invalid.Message }),
        RfqMutationResult.InvalidCategory => Results.BadRequest(new { error = "invalid_category" }),
        RfqMutationResult.InvalidUnitOfMeasure => Results.BadRequest(new { error = "invalid_unit_of_measure" }),
        RfqMutationResult.InvalidEvaluationTemplate invalid => Results.BadRequest(new { error = "invalid_evaluation_template", message = invalid.Message }),
        RfqMutationResult.SupplierNotActive => Results.BadRequest(new { error = "supplier_not_active" }),
        _ => Results.Problem(),
    };

    public static void MapRfqEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/rfqs").WithTags("Rfqs");

        group.MapGet("/", async (string? cursor, int? pageSize, bool? withCount, HttpContext httpContext, IListRfqsHandler handler, CancellationToken ct) =>
            ListResponse.Ok(httpContext, await handler.HandleAsync(cursor, pageSize, withCount == true, ct), pageSize))
        // rfq.read, not rfq.create: procurement_manager must approve RFQs (BUSINESS-PROCESSES.md
        // §3.1) and holds no authoring permission, so gating a read on create locked the approver
        // out of the list they approve from.
        .RequirePermission(Permissions.RfqRead)
        // §6.3 gives "-publishedAt" as its worked example of an RFQ list default. It cannot be this
        // list's key: this is the BUYER's list, which is mostly Drafts, and a draft has no
        // PublishedAt - a keyset on a nullable column silently drops every row where it is null.
        // -createdAt is the documented divergence, and is total over the same set.
        .WithListQuery(ListQueryPolicy.Create("-createdAt", ["createdAt"]))
        .WithName("ListRfqs");

        group.MapGet("/{referenceCode}", async (string referenceCode, IGetRfqHandler handler, CancellationToken ct) =>
        {
            var rfq = await handler.HandleAsync(referenceCode, ct);
            return rfq is null ? Results.NotFound() : Results.Ok(rfq);
        })
        .RequirePermission(Permissions.RfqRead)
        .WithName("GetRfq");

        group.MapPost("/", async (
            RfqBasicsRequest request,
            IValidator<RfqBasicsRequest> validator,
            ICreateRfqHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

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
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var result = await handler.HandleAsync(new UpdateRfqBasicsCommand(
                referenceCode, request.TitleAr, request.TitleEn, request.DescriptionAr, request.DescriptionEn,
                request.CurrencyCode, request.PublishAt, request.SubmissionOpensAt, request.SubmissionClosesAt,
                request.ClarificationDeadlineAt, request.EvaluationTargetDate), ct);
            return MapMutation(result);
        })
        .RequirePermission(Permissions.RfqEdit)
        .WithName("UpdateRfqBasics");

        group.MapPost("/{referenceCode}/items", async (
            string referenceCode,
            RfqItemRequest request,
            IValidator<RfqItemRequest> validator,
            IManageRfqItemHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

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
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

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
            var storageKey = $"rfq-attachments/{referenceCode}/{Guid.CreateVersion7()}-{file.FileName}";

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

        group.MapDelete("/{referenceCode}/attachments/{attachmentId:guid}", async (
            string referenceCode, Guid attachmentId, IManageRfqAttachmentHandler handler, CancellationToken ct) =>
            MapMutation(await handler.RemoveAsync(new RemoveRfqAttachmentCommand(referenceCode, attachmentId), ct)))
        .RequirePermission(Permissions.RfqEdit)
        .WithName("RemoveRfqAttachment");

        group.MapPut("/{referenceCode}/evaluation-template", async (
            string referenceCode, BindEvaluationTemplateRequest request, IBindEvaluationTemplateHandler handler, CancellationToken ct) =>
            MapMutation(await handler.HandleAsync(new BindEvaluationTemplateCommand(referenceCode, request.EvaluationTemplateId), ct)))
        .RequirePermission(Permissions.RfqEdit)
        .WithName("BindEvaluationTemplate");

        group.MapPost("/{referenceCode}/submit-review", async (
            string referenceCode, ISubmitRfqForReviewHandler handler, CancellationToken ct) =>
            MapMutation(await handler.HandleAsync(new SubmitRfqForReviewCommand(referenceCode), ct)))
        .RequirePermission(Permissions.RfqSubmitReview)
        .WithName("SubmitRfqForReview");

        group.MapPost("/{referenceCode}/return", async (
            string referenceCode,
            ReturnForEditsRequest request,
            IValidator<ReturnForEditsRequest> validator,
            IReturnRfqForEditsHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            return MapMutation(await handler.HandleAsync(new ReturnRfqForEditsCommand(referenceCode, request.Comments), ct));
        })
        .RequirePermission(Permissions.RfqReview)
        .WithName("ReturnRfqForEdits");

        group.MapPost("/{referenceCode}/approve", async (
            string referenceCode, IApproveRfqHandler handler, CancellationToken ct) =>
            MapMutation(await handler.HandleAsync(new ApproveRfqCommand(referenceCode), ct)))
        .RequirePermission(Permissions.RfqApprove)
        .WithName("ApproveRfq");

        group.MapPost("/{referenceCode}/publish", async (
            string referenceCode, IPublishRfqHandler handler, CancellationToken ct) =>
            MapMutation(await handler.HandleAsync(new PublishRfqCommand(referenceCode), ct)))
        .RequirePermission(Permissions.RfqPublish)
        .WithName("PublishRfq");

        group.MapPost("/{referenceCode}/close", async (
            string referenceCode, CloseSubmissionRequest request, ICloseRfqSubmissionHandler handler, CancellationToken ct) =>
            MapMutation(await handler.HandleAsync(new CloseRfqSubmissionCommand(referenceCode, request.Reason), ct)))
        .RequirePermission(Permissions.RfqClose)
        .WithName("CloseRfqSubmission");

        group.MapPost("/{referenceCode}/cancel", async (
            string referenceCode,
            CancelRfqRequest request,
            IValidator<CancelRfqRequest> validator,
            ICancelRfqHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            return MapMutation(await handler.HandleAsync(new CancelRfqCommand(referenceCode, request.Reason), ct));
        })
        .RequirePermission(Permissions.RfqCancel)
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
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

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
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            return MapMutation(await handler.HandleAsync(new IssueAddendumCommand(referenceCode, request.TitleAr, request.TitleEn, request.DescriptionAr, request.DescriptionEn), ct));
        })
        .RequirePermission(Permissions.RfqAddendum)
        .WithName("IssueAddendum");
    }
}
