// The tender routes: the list, the detail, the content edits, the invitations, the clarifications, the
// addenda, and every state change from draft through to award approval.
//
// Each state change is gated on the permission the written process table names for that actor.
//
//
// ONE COLLECTION, TWO PERSONAS
//
// The list and the detail serve both a buyer and a supplier. The contract heads this route as the
// supplier-facing list of tenders they were invited to, documents a buyer-only transition in the same
// section, and says the visible fields depend on who is asking. So the route converges and the caller's own
// scope decides, rather than there being two addresses.
//
// The route converges and the handlers do not. Each persona keeps its own handler and its own response
// shape, and the route only chooses between them.
//
// That is deliberate. A single handler deciding field by field what to include would make a cross-persona
// leak a run-time branch, where today it is structurally impossible: the supplier's handler has no code path
// that can emit a buyer-only field, because its response shape has no such member. Convergence was required
// by the contract; giving up that property was not.
//
// Both are gated on reading tenders rather than creating them. A procurement manager must approve tenders and
// holds no authoring permission, so gating a read on authoring locked the approver out of the list they
// approve from. The supplier roles hold the read permission too, because the permission is the gate and the
// row scoping is the filter.
//
// The list is ordered newest-created-first. The contract's own worked example gives newest-published, and that
// cannot be this list's key: this is the buyer's list, which is mostly drafts, and a draft has never been
// published, so paging on that column silently drops every row where it is empty. Creation time is the
// recorded divergence and it is total over the same set.
//
//
// THE TWO HAND-PARSED FILTERS
//
// The count flag and the owner filter are parsed here rather than bound, for the reason every other list in
// this API gives: bound directly, an unreadable value is refused as a malformed body, which names no field on
// a request that has no body.
//
// The owner filter is validated for every caller rather than only on the buyer's branch. A supplier who sends
// it must be told the filter is not theirs rather than served a list that quietly ignored it. The supplier's
// list has no owner to filter on at all, because ownership is a buyer-internal fact that stays inside the
// buying organization, so it is refused rather than ignored.
// What a caller may send is in RfqRequests, and how a handler's answer becomes a status is in
// RfqResults. Both used to be in this file, above the routes.
//
//
// TWO INVENTED PATHS, REPORTED AS SUCH
//
// Declining an invitation hangs off the invitations sub-resource. The contract names that sub-resource and
// makes state changes posts on a sub-resource, and names no decline transition anywhere, so this composes two
// documented rules rather than transcribing a documented path: the invitation is what is being declined.
//
// The supplier's clarification question moved onto this collection from the supplier prefix, because the
// contract lists clarifications as a sub-resource of a tender, and the buyer's answer and publish routes were
// already here.
//
//
// CORRECTING A LINE
//
// Updating an item is an update rather than a second create, because correcting a line is the same line with
// better values. The domain had add and remove with nothing between them, so a mistyped quantity could only
// be fixed by deleting the line, which renumbers every line after it.
//
//
// ATTACHMENTS
//
// Files are stored directly, with no virus-scanning quarantine step. That pipeline belongs to supplier
// documents, and scanning generally is tagged as needing business confirmation, so this is a deliberate scope
// decision rather than a silently skipped security step.
//
// The read of an attachment did not exist for a while, so a buyer could attach the specification an invited
// supplier is meant to bid against and that supplier could never open it.
//
// It is gated on reading tenders rather than editing them, because an invited supplier must reach it and
// holds no editing permission on a buyer's tender. The row scoping is the handler's: it means your own
// organization's tender for staff, and a tender you were invited to once it is published for a supplier, and
// neither is expressible as a declarative rule.
//
//
// THREE ROUTES WITHOUT A PERMISSION FILTER, EACH FOR ITS OWN REASON
//
// Moving a deadline has none, and this was got wrong first. Extending is the officer's under the edit
// permission and shortening is the manager's under its own, and a manager does not hold the edit permission.
// A route requiring it therefore refused the manager before the handler ran, which is the very caller the rule
// names for shortening. There is no any-of-these-permissions filter in this codebase, and adding one to
// express a rule that is really about direction would be the wrong shape. Both checks live in the handler,
// which is the only place the direction is known.
//
//
// TWO TRANSITIONS WITHOUT A WRITE PRECONDITION
//
// The two evaluation-stage clarification transitions deliberately do not require one, which is an exception
// to the rule rather than an oversight.
//
// The process table names an evaluator as an actor for them, and an evaluator holds no tender-read permission
// and does not necessarily belong to an organization at all, so they cannot read the tender, cannot obtain its
// version, and would be refused on a route the process document says is theirs.
//
// The guard exists to stop lost updates, and neither of these transitions carries state another writer could
// overwrite. Reported as the permission gap it is rather than closed by widening what an evaluator can read.
//
//
// THE MISSING PRECONDITION RESPONSE ON TEMPLATE BINDING
//
// Binding an evaluation template is a child write like items, requirements, attachments and invitations, all
// of which return the new version. Binding changes the tender, so the interface drops the version it was
// holding, and with no fresh one to put back the next guarded action found nothing and was refused.
//
// That next action is submitting for review, and binding a template is a precondition of it, so the two
// always happen in that order and a tender could never leave draft through the interface. It was the same
// omission as one on the supplier's legal information, not the deliberate split in this file between child
// writes and state changes.
//
//
// THE BIDS RECEIVED
//
// The list of bids against a tender and the read of one sit behind the comparison permission, which already
// means "may see bid-level data", rather than a new one. Inventing a second permission for the same class of
// data puts the rule in two places and they drift. What limits the answer is which stage the evaluation has
// reached, not the gate.
//
// One bid is addressed by its internal identifier, because that is the identifier a buyer holds. The
// divergence from the rule that internal identifiers stay out of addresses is recorded where the buyer's
// document read records it, rather than widened by inventing a second addressing scheme for one route.
//
//
// OWNERSHIP
//
// Reassigning requires a write precondition, because ownership is a field on the tender and moving it is a
// write that must not overwrite a concurrent one.
//
//
// WRITE PRECONDITIONS ON THE CHILD WRITES
//
// Every buyer-side write that adds or removes a part of the tender, an item, a requirement, an
// attachment or an invitation, requires the caller to say which version they read, and returns the
// version it produced.
//
// The tender's own version moves either way, so the only question is whether anybody is told. Without
// the precondition an officer can add an item on top of a tender they never saw: one a manager has just
// returned for edits, or whose deadline has moved underneath them, and the write succeeds against a
// state nobody was looking at. On a tender that is exactly the lost update the rule exists to refuse.
//
// The precondition is obtainable, which is the test any guard here has to pass: the tender's own read
// issues the version, and the interface walks up the path to find it.
//
// These use the fresh-version filter rather than the read one, because the read filter also answers a
// conditional read with not-modified, and a not-modified answer on a write that changed the record would
// be a lie about what happened.
//
//
// FOUR WRITES ON THIS COLLECTION ARE DELIBERATELY LEFT UNGUARDED
//
// Creating a tender has no prior version anybody could have read, so requiring one would make authoring
// impossible.
//
// The supplier's two writes, asking a clarification and declining an invitation, are unguarded because
// the supplier's own view of a tender carries no version at all. That is deliberate: the version belongs
// to the buyer's record. So the precondition is unobtainable and the guard would refuse every supplier.
// It would also be guarding the wrong thing, because two invited suppliers asking unrelated questions is
// not a lost update, and refusing the second is a fairness problem rather than a safety one.
//
// The two evaluation-stage clarification transitions are covered above: an evaluator cannot read the
// tender, so they cannot obtain a precondition either.
//
//
// PRECONDITION RESPONSES
//
// Every guarded write and every transition puts the new version on its own response, so the next one has a
// precondition to send without waiting for a re-read.

namespace MotsSupplierPortal.Api.Endpoints;

using MotsSupplierPortal.Api.Concurrency;
using MotsSupplierPortal.Api.Errors;
using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Infrastructure.Storage;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Identity;

public static class RfqEndpoints
{
    public static void MapRfqEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/rfqs").WithTags("Rfqs");

        group.MapGet("/", async (
            string? cursor, int? pageSize, string? withCount, string? owner, HttpContext httpContext,
            IScopeContext scope,
            IListRfqsHandler buyerHandler,
            ISupplierListInvitedRfqsHandler supplierHandler,
            CancellationToken ct) =>
        {
            if (!FilterValues.TryParseBoolFilter(withCount, out _, out var badWithCount))
            {
                return FilterValues.InvalidFilterValue("withCount", badWithCount!);
            }

            if (!FilterValues.IsAllowedLiteralOrGuid(owner, RfqListFilterValues.OwnerLiterals, out var invalidOwner))
            {
                return FilterValues.InvalidFilterValue("owner", invalidOwner!);
            }

            var wantsCount = FilterValues.BoolOrFalse(withCount);
            if (scope.SupplierId is not null)
            {
                if (owner is not null) return FilterValues.InvalidFilterValue("owner", owner);
                return ListResponse.Ok(httpContext, await supplierHandler.HandleAsync(cursor, pageSize, wantsCount, ct), pageSize);
            }

            return ListResponse.Ok(httpContext, await buyerHandler.HandleAsync(cursor, pageSize, wantsCount, owner, ct), pageSize);
        })
        .RequirePermission(Permissions.RfqRead)
        .WithListQuery(ListQueryPolicy.Create("-createdAt", ["createdAt"], "owner"))
        .WithName("ListRfqs");

        group.MapGet("/{referenceCode}", async (
            string referenceCode,
            IScopeContext scope,
            IGetRfqHandler buyerHandler,
            ISupplierGetRfqHandler supplierHandler,
            CancellationToken ct) =>
        {
            if (scope.SupplierId is not null)
            {
                return RfqResults.MapSupplierResult(await supplierHandler.HandleAsync(referenceCode, ct));
            }

            var rfq = await buyerHandler.HandleAsync(referenceCode, ct);
            return rfq is null ? Results.NotFound() : Results.Ok(rfq);
        })
        .RequirePermission(Permissions.RfqRead)
        .WithETag()
        .WithName("GetRfq");

        group.MapPost("/{referenceCode}/clarifications", async (
            string referenceCode,
            PostClarificationRequest request,
            IValidator<PostClarificationRequest> validator,
            ISupplierPostClarificationHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            return RfqResults.MapSupplierResult(await handler.HandleAsync(new PostClarificationQuestionCommand(referenceCode, request.Question), ct));
        })
        .RequirePermission(Permissions.ProposalCreate)
        .WithName("SupplierPostClarification");

        group.MapPost("/{referenceCode}/invitations/decline", async (
            string referenceCode, DeclineInvitationRequest request, ISupplierDeclineInvitationHandler handler, CancellationToken ct) =>
            RfqResults.MapSupplierResult(await handler.HandleAsync(new DeclineInvitationCommand(referenceCode, request.Reason), ct)))
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
            return RfqResults.MapMutation(result);
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
            return RfqResults.MapMutation(result);
        })
        .RequirePermission(Permissions.RfqEdit)
        .RequireIfMatch()
        .WithFreshETag()
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
            return RfqResults.MapMutation(result);
        })
        .RequirePermission(Permissions.RfqEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("AddRfqItem");

        group.MapPut("/{referenceCode}/items/{itemId:guid}", async (
            string referenceCode,
            Guid itemId,
            RfqItemRequest request,
            IValidator<RfqItemRequest> validator,
            IManageRfqItemHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.UpdateAsync(new UpdateRfqItemCommand(
                referenceCode, itemId, request.TitleAr, request.TitleEn, request.SpecificationAr, request.SpecificationEn,
                request.CategoryCode, request.Quantity, request.UnitOfMeasureCode, request.IsUnitPrice, request.IsOptional), ct);
            return RfqResults.MapMutation(result);
        })
        .RequirePermission(Permissions.RfqEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("UpdateRfqItem");

        group.MapDelete("/{referenceCode}/items/{itemId:guid}", async (
            string referenceCode, Guid itemId, IManageRfqItemHandler handler, CancellationToken ct) =>
            RfqResults.MapMutation(await handler.RemoveAsync(new RemoveRfqItemCommand(referenceCode, itemId), ct)))
        .RequirePermission(Permissions.RfqEdit)
        .RequireIfMatch()
        .WithFreshETag()
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
            return RfqResults.MapMutation(result);
        })
        .RequirePermission(Permissions.RfqEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("AddRequirement");

        group.MapPut("/{referenceCode}/requirements/{requirementId:guid}", async (
            string referenceCode,
            Guid requirementId,
            RequirementRequest request,
            IValidator<RequirementRequest> validator,
            IManageRequirementHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.UpdateAsync(new UpdateRequirementCommand(
                referenceCode, requirementId, request.TextAr, request.TextEn, request.IsMandatory,
                request.DocumentTypeCode, request.ExpectedEnvelope), ct);
            return RfqResults.MapMutation(result);
        })
        .RequirePermission(Permissions.RfqEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("UpdateRequirement");

        group.MapDelete("/{referenceCode}/requirements/{requirementId:guid}", async (
            string referenceCode, Guid requirementId, IManageRequirementHandler handler, CancellationToken ct) =>
            RfqResults.MapMutation(await handler.RemoveAsync(new RemoveRequirementCommand(referenceCode, requirementId), ct)))
        .RequirePermission(Permissions.RfqEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("RemoveRequirement");

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
            var storageKey = AttachmentStorageKey.For(AttachmentStorageKey.RfqAttachmentPrefix);

            await using (var stream = file.OpenReadStream())
            {
                await fileStorage.SaveAsync(storageKey, stream, file.ContentType, ct);
            }

            var result = await handler.AddAsync(new AddRfqAttachmentCommand(
                referenceCode, storageKey, file.FileName, file.ContentType, string.IsNullOrWhiteSpace(caption) ? null : caption), ct);
            return RfqResults.MapMutation(result);
        })
        .RequirePermission(Permissions.RfqEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("AddRfqAttachment");

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
            RfqResults.MapMutation(await handler.RemoveAsync(new RemoveRfqAttachmentCommand(referenceCode, attachmentId), ct)))
        .RequirePermission(Permissions.RfqEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("RemoveRfqAttachment");

        group.MapPut("/{referenceCode}/evaluation-template", async (
            string referenceCode, BindEvaluationTemplateRequest request, IBindEvaluationTemplateHandler handler, CancellationToken ct) =>
            RfqResults.MapMutation(await handler.HandleAsync(new BindEvaluationTemplateCommand(referenceCode, request.EvaluationTemplateId), ct)))
        .RequirePermission(Permissions.RfqEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("BindEvaluationTemplate");

        group.MapPost("/{referenceCode}/deadline", async (
            string referenceCode, ChangeSubmissionDeadlineRequest request,
            IValidator<ChangeSubmissionDeadlineRequest> validator,
            IChangeSubmissionDeadlineHandler handler, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            return RfqResults.MapMutation(await handler.HandleAsync(
                new ChangeSubmissionDeadlineCommand(referenceCode, request.SubmissionDeadline, request.Reason), ct));
        })
        .RequireAuthorization()
        .RequireIfMatch()
        .WithETag()
        .WithFreshETag()
.WithName("ChangeSubmissionDeadline");

        group.MapPost("/{referenceCode}/submit-review", async (
            string referenceCode, SubmitForReviewRequest? request, ISubmitRfqForReviewHandler handler, CancellationToken ct) =>
            RfqResults.MapMutation(await handler.HandleAsync(
                new SubmitRfqForReviewCommand(referenceCode, request?.AssignedApproverUserId), ct)))
        .RequirePermission(Permissions.RfqSubmitReview)
        .RequireIfMatch()
        .WithFreshETag()
.WithName("SubmitRfqForReview");

        group.MapGet("/{referenceCode}/received-proposals", async (
            string referenceCode, IListBuyerProposalsHandler handler, CancellationToken ct) =>
        {
            var proposals = await handler.HandleAsync(referenceCode, ct);
            return proposals is null ? Results.NotFound() : Results.Ok(proposals);
        })
        .RequirePermission(Permissions.ComparisonView)
        .WithName("ListReceivedProposals");

        group.MapGet("/{referenceCode}/received-proposals/{proposalId:guid}", async (
            string referenceCode, Guid proposalId, IGetBuyerProposalHandler handler, CancellationToken ct) =>
        {
            var proposal = await handler.HandleAsync(referenceCode, proposalId, ct);
            return proposal is null ? Results.NotFound() : Results.Ok(proposal);
        })
        .RequirePermission(Permissions.ComparisonView)
        .WithName("GetReceivedProposal");

        group.MapGet("/{referenceCode}/assignees", async (
            string referenceCode, IListRfqAssigneesHandler handler, CancellationToken ct) =>
        {
            var assignees = await handler.HandleAsync(referenceCode, ct);
            return assignees is null ? Results.NotFound() : Results.Ok(assignees);
        })
        .RequirePermission(Permissions.RfqRead)
        .WithName("ListRfqAssignees");

        group.MapPost("/{referenceCode}/reassign", async (
            string referenceCode,
            ReassignRfqRequest request,
            IValidator<ReassignRfqRequest> validator,
            IReassignRfqHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            return RfqResults.MapMutation(await handler.HandleAsync(
                new ReassignRfqCommand(referenceCode, request.NewOwnerUserId, request.Reason), ct));
        })
        .RequirePermission(Permissions.RfqReassign)
        .RequireIfMatch()
        .WithFreshETag()
.WithName("ReassignRfq");

        group.MapPost("/{referenceCode}/return", async (
            string referenceCode,
            ReturnForEditsRequest request,
            IValidator<ReturnForEditsRequest> validator,
            IReturnRfqForEditsHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            return RfqResults.MapMutation(await handler.HandleAsync(new ReturnRfqForEditsCommand(referenceCode, request.Comments), ct));
        })
        .RequirePermission(Permissions.RfqReview)
        .RequireIfMatch()
        .WithFreshETag()
.WithName("ReturnRfqForEdits");

        group.MapPost("/{referenceCode}/approve", async (
            string referenceCode, IApproveRfqHandler handler, CancellationToken ct) =>
            RfqResults.MapMutation(await handler.HandleAsync(new ApproveRfqCommand(referenceCode), ct)))
        .RequirePermission(Permissions.RfqApprove)
        .RequireIfMatch()
        .WithFreshETag()
.WithName("ApproveRfq");

        group.MapPost("/{referenceCode}/publish", async (
            string referenceCode, IPublishRfqHandler handler, CancellationToken ct) =>
            RfqResults.MapMutation(await handler.HandleAsync(new PublishRfqCommand(referenceCode), ct)))
        .RequirePermission(Permissions.RfqPublish)
        .RequireIfMatch()
        .RequireIdempotencyKey()
        .WithFreshETag()
.WithName("PublishRfq");

        group.MapPost("/{referenceCode}/close", async (
            string referenceCode, CloseSubmissionRequest request, ICloseRfqSubmissionHandler handler, CancellationToken ct) =>
            RfqResults.MapMutation(await handler.HandleAsync(new CloseRfqSubmissionCommand(referenceCode, request.Reason), ct)))
        .RequirePermission(Permissions.RfqClose)
        .RequireIfMatch()
        .WithFreshETag()
.WithName("CloseRfqSubmission");

        group.MapPost("/{referenceCode}/request-clarification", async (
            string referenceCode, RequestClarificationTransitionRequest request,
            IValidator<RequestClarificationTransitionRequest> validator,
            IRequestRfqClarificationHandler handler, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            return RfqResults.MapMutation(await handler.HandleAsync(new RequestRfqClarificationCommand(referenceCode, request.Reason), ct));
        })
        .RequirePermission(Permissions.RfqClarify)
        .WithName("RequestRfqClarification");

        group.MapPost("/{referenceCode}/resolve-clarification", async (
            string referenceCode, IResolveRfqClarificationHandler handler, CancellationToken ct) =>
            RfqResults.MapMutation(await handler.HandleAsync(new ResolveRfqClarificationCommand(referenceCode), ct)))
        .RequirePermission(Permissions.RfqClarify)
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

            return RfqResults.MapMutation(await handler.HandleAsync(new CancelRfqCommand(referenceCode, request.Reason), ct));
        })
        .RequirePermission(Permissions.RfqCancel)
        .RequireIfMatch()
        .WithFreshETag()
.WithName("CancelRfq");

        group.MapPost("/{referenceCode}/invitations", async (
            string referenceCode, InviteSupplierRequest request, IInviteSupplierHandler handler, CancellationToken ct) =>
            RfqResults.MapMutation(await handler.HandleAsync(new InviteSupplierCommand(referenceCode, request.SupplierId), ct)))
        .RequirePermission(Permissions.RfqInvite)
        .RequireIfMatch()
        .WithFreshETag()
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

            return RfqResults.MapMutation(await handler.HandleAsync(new AnswerClarificationCommand(referenceCode, clarificationId, request.Answer), ct));
        })
        .RequirePermission(Permissions.ClarificationAnswer)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("AnswerClarification");

        group.MapPost("/{referenceCode}/clarifications/{clarificationId:guid}/publish", async (
            string referenceCode, Guid clarificationId, IPublishClarificationHandler handler, CancellationToken ct) =>
            RfqResults.MapMutation(await handler.HandleAsync(new PublishClarificationCommand(referenceCode, clarificationId), ct)))
        .RequirePermission(Permissions.ClarificationAnswer)
        .RequireIfMatch()
        .WithFreshETag()
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

            return RfqResults.MapMutation(await handler.HandleAsync(new IssueAddendumCommand(referenceCode, request.TitleAr, request.TitleEn, request.DescriptionAr, request.DescriptionEn), ct));
        })
        .RequirePermission(Permissions.RfqAddendum)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("IssueAddendum");
    }
}
