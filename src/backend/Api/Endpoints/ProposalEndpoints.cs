using MotsSupplierPortal.Api.Errors;
using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Api.Endpoints;

public sealed record SetItemPricingRequest(decimal Quantity, decimal UnitPrice, decimal? Discount, int? LeadTimeDays, string? NotesAr, string? NotesEn);

public sealed class SetItemPricingRequestValidator : AbstractValidator<SetItemPricingRequest>
{
    public SetItemPricingRequestValidator()
    {
        RuleFor(x => x.Quantity).GreaterThan(0);
        // §7.2 documents this rule by name and by message: PRICE_NON_POSITIVE, «يجب أن يكون سعر
        // الوحدة أكبر من صفر». It was GreaterThanOrEqualTo(0), so a zero-price bid line was accepted
        // while the contract said it could not be - ruled in favour of the contract.
        RuleFor(x => x.UnitPrice).GreaterThan(0);
    }
}

public sealed record SetCommercialTermsRequest(
    string CurrencyCode, string? PaymentTerms, string? IncotermCode,
    string? DeliveryTermsAr, string? DeliveryTermsEn, string? Warranty, DateOnly? ValidityStart, DateOnly? ValidityEnd);

public sealed class SetCommercialTermsRequestValidator : AbstractValidator<SetCommercialTermsRequest>
{
    public SetCommercialTermsRequestValidator() => RuleFor(x => x.CurrencyCode).NotEmpty().MaximumLength(3);
}

public sealed record SetNarrativeRequest(string? NarrativeAr, string? NarrativeEn);

public sealed record AnswerRequirementRequest(string AnswerAr, string AnswerEn);

public sealed class AnswerRequirementRequestValidator : AbstractValidator<AnswerRequirementRequest>
{
    public AnswerRequirementRequestValidator()
    {
        RuleFor(x => x.AnswerAr).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.AnswerEn).NotEmpty().MaximumLength(4000);
    }
}

public sealed record WithdrawProposalRequest(string Reason);

public sealed class WithdrawProposalRequestValidator : AbstractValidator<WithdrawProposalRequest>
{
    public WithdrawProposalRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
}

/// <summary>FEAT-09.1..09.6/FR-PRP-001..008: the supplier's own proposal against one RFQ - nested
/// under the same "/api/v1/suppliers/me/rfqs/{referenceCode}" base SupplierRfqEndpoints already
/// uses, since a Proposal only ever makes sense in the context of the RFQ it answers, even though
/// it is its own aggregate root (Proposal.cs's own doc comment). Every handler resolves the caller's
/// own Proposal by their own SupplierId - there is no route here, or anywhere in this codebase yet,
/// that can return another supplier's Proposal (FEAT-09.8/FR-PRP-012).
///
/// <para>Permissions follow BUSINESS-PROCESSES.md §4.1's own actor column exactly: ProposalCreate
/// for start/view (both supplier roles), ProposalEdit for Draft content (both), ProposalSubmit for
/// submit (supplier_admin only), ProposalWithdraw for withdraw (supplier_admin only).</para></summary>
public static class ProposalEndpoints
{
    private static IResult MapResult(ProposalResult result) => result switch
    {
        ProposalResult.Success s => Results.Ok(s.Proposal),
        ProposalResult.NotFoundOrNotInvited => Results.NotFound(),
        ProposalResult.InvalidState invalid => Results.BadRequest(new { error = "invalid_state", message = invalid.Message }),
        _ => Results.Problem(),
    };

    public static void MapProposalEndpoints(this IEndpointRouteBuilder app)
    {
        // §12-A/C2. Two collections, per §3 and §12.5:
        //  - creation and discovery hang off the RFQ ("/rfqs/{rfqCode}/proposals", §3's own
        //    sub-resource example, and §12.5's "POST /rfqs/{rfqCode}/proposals" heading);
        //  - everything addressing an EXISTING proposal is top-level by its own public code
        //    ("/proposals/{proposalCode}/items" in §3, "PATCH /proposals/{proposalCode}" and
        //    "POST /proposals/{proposalCode}/submit" in §12.5).
        //
        // The six edit sub-routes below move with the tree but keep their current shape - §12.5's
        // collapse into one JSON Merge Patch is the next batch, deliberately not started here.
        var rfqScoped = app.MapGroup("/api/v1/rfqs/{referenceCode}/proposals").WithTags("Proposals");
        var group = app.MapGroup("/api/v1/proposals/{referenceCode}").WithTags("Proposals");

        rfqScoped.MapPost("/", async (string referenceCode, IStartProposalHandler handler, CancellationToken ct) =>
            MapResult(await handler.HandleAsync(referenceCode, ct)))
        .RequirePermission(Permissions.ProposalCreate)
        .WithName("StartProposal");

        rfqScoped.MapGet("/", async (string referenceCode, IGetProposalHandler handler, CancellationToken ct) =>
            MapResult(await handler.HandleAsync(referenceCode, ct)))
        .RequirePermission(Permissions.ProposalCreate)
        .WithName("GetProposal");

        // §12-A/C2: the code-addressed read. §3 addresses a proposal's sub-resources at
        // /proposals/{proposalCode}/…, so the resource itself must be readable there too;
        // §12 documents PATCH and submit on this path but no GET, so the GET is an invention.
        group.MapGet("/", async (string referenceCode, IGetProposalByCodeHandler handler, CancellationToken ct) =>
            MapResult(await handler.HandleAsync(referenceCode, ct)))
        .RequirePermission(Permissions.ProposalCreate)
        .WithName("GetProposalByCode");

        group.MapPut("/terms", async (
            string referenceCode,
            SetCommercialTermsRequest request,
            IValidator<SetCommercialTermsRequest> validator,
            ISetCommercialTermsHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            return MapResult(await handler.HandleAsync(new SetCommercialTermsCommand(
                referenceCode, request.CurrencyCode, request.PaymentTerms, request.IncotermCode,
                request.DeliveryTermsAr, request.DeliveryTermsEn, request.Warranty, request.ValidityStart, request.ValidityEnd), ct));
        })
        .RequirePermission(Permissions.ProposalEdit)
        .WithName("SetProposalCommercialTerms");

        group.MapPut("/narrative", async (
            string referenceCode, SetNarrativeRequest request, ISetNarrativeHandler handler, CancellationToken ct) =>
            MapResult(await handler.HandleAsync(new SetNarrativeCommand(referenceCode, request.NarrativeAr, request.NarrativeEn), ct)))
        .RequirePermission(Permissions.ProposalEdit)
        .WithName("SetProposalNarrative");

        group.MapPut("/items/{rfqItemId:guid}", async (
            string referenceCode,
            Guid rfqItemId,
            SetItemPricingRequest request,
            IValidator<SetItemPricingRequest> validator,
            IManageProposalItemHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            return MapResult(await handler.SetAsync(new SetItemPricingCommand(
                referenceCode, rfqItemId, request.Quantity, request.UnitPrice, request.Discount, request.LeadTimeDays, request.NotesAr, request.NotesEn), ct));
        })
        .RequirePermission(Permissions.ProposalEdit)
        .WithName("SetProposalItemPricing");

        group.MapDelete("/items/{rfqItemId:guid}", async (
            string referenceCode, Guid rfqItemId, IManageProposalItemHandler handler, CancellationToken ct) =>
            MapResult(await handler.RemoveAsync(new RemoveItemPricingCommand(referenceCode, rfqItemId), ct)))
        .RequirePermission(Permissions.ProposalEdit)
        .WithName("RemoveProposalItemPricing");

        group.MapPost("/requirements/{requirementId:guid}/answer", async (
            string referenceCode,
            Guid requirementId,
            AnswerRequirementRequest request,
            IValidator<AnswerRequirementRequest> validator,
            IAnswerRequirementHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            return MapResult(await handler.HandleAsync(new AnswerRequirementCommand(referenceCode, requirementId, request.AnswerAr, request.AnswerEn), ct));
        })
        .RequirePermission(Permissions.ProposalEdit)
        .WithName("AnswerProposalRequirement");

        // FEAT-09.3/FR-PRP-004: same inline IFileStorage pattern as RfqEndpoints' attachment upload
        // (no AV-scan quarantine flow here either - see ManageProposalDocumentHandler's own comment).
        group.MapPost("/documents", async (
            string referenceCode,
            HttpRequest request,
            IFileStorage fileStorage,
            IManageProposalDocumentHandler handler,
            CancellationToken ct) =>
        {
            if (!request.HasFormContentType) return Results.BadRequest(new { error = "expected_multipart_form" });

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0) return Results.BadRequest(new { error = "file_required" });

            var caption = form["caption"].ToString();
            var storageKey = $"proposal-documents/{referenceCode}/{Guid.CreateVersion7()}-{file.FileName}";

            await using (var stream = file.OpenReadStream())
            {
                await fileStorage.SaveAsync(storageKey, stream, file.ContentType, ct);
            }

            return MapResult(await handler.AddAsync(new AddProposalDocumentCommand(
                referenceCode, storageKey, file.FileName, file.ContentType, string.IsNullOrWhiteSpace(caption) ? null : caption), ct));
        })
        .RequirePermission(Permissions.ProposalEdit)
        .WithName("AddProposalDocument");

        group.MapDelete("/documents/{documentId:guid}", async (
            string referenceCode, Guid documentId, IManageProposalDocumentHandler handler, CancellationToken ct) =>
            MapResult(await handler.RemoveAsync(new RemoveProposalDocumentCommand(referenceCode, documentId), ct)))
        .RequirePermission(Permissions.ProposalEdit)
        .WithName("RemoveProposalDocument");

        group.MapPost("/submit", async (string referenceCode, ISubmitProposalHandler handler, CancellationToken ct) =>
            MapResult(await handler.HandleAsync(new SubmitProposalCommand(referenceCode), ct)))
        .RequirePermission(Permissions.ProposalSubmit)
        .WithName("SubmitProposal");

        group.MapPost("/withdraw", async (
            string referenceCode,
            WithdrawProposalRequest request,
            IValidator<WithdrawProposalRequest> validator,
            IWithdrawProposalHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            return MapResult(await handler.HandleAsync(new WithdrawProposalCommand(referenceCode, request.Reason), ct));
        })
        .RequirePermission(Permissions.ProposalWithdraw)
        .WithName("WithdrawProposal");
    }
}
