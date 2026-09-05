using System.Text.Json.Nodes;
using MotsSupplierPortal.Api.Concurrency;
using MotsSupplierPortal.Api.Errors;
using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Infrastructure.Storage;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Domain.Proposals;
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

// T-064: same shape and same bound as a withdrawal - both are a supplier ending their own
// participation and both owe an explanation to the record.
public sealed record DeclineAwardOfferRequest(string Reason);

public sealed class DeclineAwardOfferRequestValidator : AbstractValidator<DeclineAwardOfferRequest>
{
    public DeclineAwardOfferRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
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
        // T-065, §3: "Illegal transitions return 409 Conflict (type: …/errors/invalid-state-transition)
        // listing the current state and the allowed next states." RFQ endpoints have answered that
        // since T3-36; proposals answered 400, so one product carried two conventions for "this
        // resource has moved on" - and batch 7 made the proposal machine something callers hit.
        //
        // A refusal with no state attached keeps its 400: those are shaped like validation ("a
        // withdrawal reason is required") and have no allowed-next set to offer. §3 governs
        // transitions, not every rejection.
        // T-066: §12.5 answers an incomplete submission with 422 and a code. The middleware turns
        // `error` into §7's SCREAMING_SNAKE code, so PROPOSAL_ITEMS_REQUIRED comes out of the
        // identifier the domain threw - no second mapping table.
        ProposalResult.Incomplete incomplete =>
            Results.UnprocessableEntity(new { error = incomplete.Error, message = incomplete.Message }),

        ProposalResult.InvalidState { CurrentState: { } state } invalid =>
            IllegalTransitionResult.For(state, invalid.Message),
        ProposalResult.InvalidState invalid => Results.BadRequest(new { error = "invalid_state", message = invalid.Message }),
        _ => Results.Problem(),
    };

    private const string MergePatchContentType = "application/merge-patch+json";

    private static IResult MapPatchResult(ProposalPatchResult result) => result switch
    {
        ProposalPatchResult.Success s => Results.Ok(s.Proposal),
        ProposalPatchResult.NotFoundOrNotInvited => Results.NotFound(),
        ProposalPatchResult.InvalidState invalid => Results.BadRequest(new { error = "invalid_state", message = invalid.Message }),
        ProposalPatchResult.Invalid bad => Results.BadRequest(new { error = bad.Code, message = bad.Detail, field = bad.Field }),
        _ => Results.Problem(),
    };


    /// <summary>
    /// Runs the retired sub-routes' validators over the merge patch, re-pathing each failure to the
    /// member it came from. Returns null when everything present is valid - including when nothing
    /// is present, which RFC 7396 makes a legitimate no-op rather than an error.
    /// </summary>
    private static async Task<IResult?> ValidatePatchAsync(
        JsonObject patch,
        IValidator<SetItemPricingRequest> itemValidator,
        IValidator<SetCommercialTermsRequest> termsValidator,
        IValidator<AnswerRequirementRequest> answerValidator,
        CancellationToken ct)
    {
        var failures = new List<FluentValidation.Results.ValidationFailure>();

        if (patch["items"] is JsonArray items)
        {
            for (var index = 0; index < items.Count; index++)
            {
                if (items[index] is not JsonObject item) continue;

                var request = new SetItemPricingRequest(
                    item["quantity"]?.GetValue<decimal>() ?? 0m,
                    item["unitPrice"]?.GetValue<decimal>() ?? 0m,
                    item["discount"]?.GetValue<decimal>(),
                    item["leadTimeDays"]?.GetValue<int>(),
                    item["notesAr"]?.GetValue<string>(),
                    item["notesEn"]?.GetValue<string>());

                AddPrefixed(failures, await itemValidator.ValidateAsync(request, ct), $"items[{index}]");
            }
        }

        if (patch["commercialTerms"] is JsonObject terms && terms["currencyCode"] is not null)
        {
            var request = new SetCommercialTermsRequest(
                terms["currencyCode"]!.GetValue<string>(),
                terms["paymentTerms"]?.GetValue<string>(), terms["incotermCode"]?.GetValue<string>(),
                terms["deliveryTermsAr"]?.GetValue<string>(), terms["deliveryTermsEn"]?.GetValue<string>(),
                terms["warranty"]?.GetValue<string>(), null, null);

            AddPrefixed(failures, await termsValidator.ValidateAsync(request, ct), "commercialTerms");
        }

        if (patch["technicalResponse"] is JsonObject response && response["answers"] is JsonArray answers)
        {
            for (var index = 0; index < answers.Count; index++)
            {
                if (answers[index] is not JsonObject answer) continue;

                var request = new AnswerRequirementRequest(
                    answer["answerAr"]?.GetValue<string>() ?? string.Empty,
                    answer["answerEn"]?.GetValue<string>() ?? string.Empty);

                AddPrefixed(failures, await answerValidator.ValidateAsync(request, ct), $"technicalResponse.answers[{index}]");
            }
        }

        return failures.Count == 0 ? null : ValidationProblems.From(new FluentValidation.Results.ValidationResult(failures));
    }

    private static void AddPrefixed(
        List<FluentValidation.Results.ValidationFailure> into,
        FluentValidation.Results.ValidationResult result,
        string prefix)
    {
        foreach (var failure in result.Errors)
        {
            into.Add(new FluentValidation.Results.ValidationFailure($"{prefix}.{failure.PropertyName}", failure.ErrorMessage)
            {
                ErrorCode = failure.ErrorCode,
                AttemptedValue = failure.AttemptedValue,
            });
        }
    }

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
        .WithETag()
        .WithName("GetProposal");

        // §12-A/C2: the code-addressed read. §3 addresses a proposal's sub-resources at
        // /proposals/{proposalCode}/…, so the resource itself must be readable there too;
        // §12 documents PATCH and submit on this path but no GET, so the GET is an invention.
        group.MapGet("/", async (string referenceCode, IGetProposalByCodeHandler handler, CancellationToken ct) =>
            MapResult(await handler.HandleAsync(referenceCode, ct)))
        .RequirePermission(Permissions.ProposalCreate)
        .WithETag()
        .WithName("GetProposalByCode");

        // §12.5: "PATCH /proposals/{proposalCode} - edit draft (line items, terms) with If-Match",
        // returning "200 OK with recomputed totals and new ETag". §4 states the rule normatively:
        // "PATCH | Partial update (JSON Merge Patch, RFC 7396) of draft-editable resources".
        //
        // This ONE route replaces five: PUT /terms, PUT /narrative, PUT /items/{id},
        // DELETE /items/{id} and POST /requirements/{id}/answer. They are retired outright rather
        // than deprecated - two ways to edit one resource is how the wrong one becomes permanent,
        // and with §8.1 in force they would also be five separate version checks over what a
        // supplier experiences as one edit.
        //
        // The body is read as a JsonNode, not a DTO, because merge patch distinguishes an ABSENT
        // member ("leave it") from an explicit null ("delete it") and a deserialised DTO cannot -
        // both arrive as null. See ProposalMergePatch.
        group.MapPatch("/", async (
            string referenceCode,
            HttpContext http,
            IPatchProposalHandler handler,
            IValidator<SetItemPricingRequest> itemValidator,
            IValidator<SetCommercialTermsRequest> termsValidator,
            IValidator<AnswerRequirementRequest> answerValidator,
            CancellationToken ct) =>
        {
            // RFC 7396 defines its own media type, and §4 names merge patch specifically. Accepting
            // application/json here would leave the semantics ambiguous at exactly the point where
            // absent-versus-null decides whether a supplier keeps their warranty text.
            var contentType = http.Request.ContentType ?? string.Empty;
            if (!contentType.StartsWith(MergePatchContentType, StringComparison.OrdinalIgnoreCase))
            {
                return new UnsupportedMediaTypeResult();
            }

            var body = await http.Request.ReadFromJsonAsync<JsonNode>(ct);
            if (body is not JsonObject patch)
            {
                return ValidationProblems.MalformedMergePatch(http);
            }

            // The sub-routes each ran a FluentValidation validator before touching the aggregate, and
            // §7.2's catalogue is keyed by those rules. Retiring the routes must not retire their
            // validation, so the same validators run here over the same shapes - with the failures
            // re-pathed to where they actually live in the patch body (items[0].unitPrice, not
            // UnitPrice), because §7.2's paths exist so the editor can map an error onto an input.
            var invalid = await ValidatePatchAsync(patch, itemValidator, termsValidator, answerValidator, ct);
            if (invalid is not null) return invalid;

            return MapPatchResult(await handler.HandleAsync(referenceCode, new ProposalMergePatch(patch), ct));
        })
        .RequirePermission(Permissions.ProposalEdit)
        .RequireIfMatch()
        .WithETag()
        .WithName("PatchProposal");

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

            // T-028/D-7: the supplier declares the envelope. Anything unparseable, absent, or
            // simply not sent stays Commercial - the parse FAILING must not be the thing that opens
            // a file up, so the fallback is the gated side rather than the last value tried.
            var envelope = Enum.TryParse<ProposalDocumentEnvelope>(form["envelope"].ToString(), ignoreCase: true, out var parsed)
                ? parsed
                : ProposalDocumentEnvelope.Commercial;
            // Server-side key, and the quarantine gap both these paths share, are explained once in
            // AttachmentStorageKey rather than twice here.
            var storageKey = AttachmentStorageKey.For(AttachmentStorageKey.ProposalDocumentPrefix);

            await using (var stream = file.OpenReadStream())
            {
                await fileStorage.SaveAsync(storageKey, stream, file.ContentType, ct);
            }

            return MapResult(await handler.AddAsync(new AddProposalDocumentCommand(
                referenceCode, storageKey, file.FileName, file.ContentType,
                string.IsNullOrWhiteSpace(caption) ? null : caption, envelope), ct));
        })
        .RequirePermission(Permissions.ProposalEdit)
        .WithName("AddProposalDocument");

        // T-028: the supplier's own read of their own file. No envelope gate - see
        // GetOwnProposalDocumentDownloadUrlHandler on why the two-envelope rule does not apply to a
        // bidder reading their own bid.
        group.MapGet("/documents/{documentId:guid}/download-url", async (
            string referenceCode, Guid documentId,
            IGetOwnProposalDocumentDownloadUrlHandler handler, CancellationToken ct) =>
            await handler.HandleAsync(referenceCode, documentId, ct) switch
            {
                ProposalDocumentDownloadResult.Success s => Results.Ok(new { url = s.Url, fileName = s.FileName }),
                _ => Results.NotFound(),
            })
        // ProposalEdit rather than ProposalCreate: both supplier roles hold it, and reading a file
        // back is strictly narrower than the upload that put it there.
        .RequirePermission(Permissions.ProposalEdit)
        .WithName("GetOwnProposalDocumentDownloadUrl");

        group.MapDelete("/documents/{documentId:guid}", async (
            string referenceCode, Guid documentId, IManageProposalDocumentHandler handler, CancellationToken ct) =>
            MapResult(await handler.RemoveAsync(new RemoveProposalDocumentCommand(referenceCode, documentId), ct)))
        .RequirePermission(Permissions.ProposalEdit)
        .WithName("RemoveProposalDocument");

        // T-064/§4.1: "AwardOffered -> Declined | Supplier declines | supplier_admin /
        // proposal.decline | Within acceptance window ([ASSUMPTION])". No window is enforced - see
        // Proposal.OfferAward and DECISIONS-TAKEN.md D-21.
        group.MapPost("/decline", async (
            string referenceCode, DeclineAwardOfferRequest request,
            IValidator<DeclineAwardOfferRequest> validator,
            IDeclineAwardOfferHandler handler, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            return MapResult(await handler.HandleAsync(
                new DeclineAwardOfferCommand(referenceCode, request.Reason), ct));
        })
        .RequirePermission(Permissions.ProposalDecline)
        .RequireIfMatch()
        .WithETag()
        .WithName("DeclineAwardOffer");

        group.MapPost("/submit", async (string referenceCode, ISubmitProposalHandler handler, CancellationToken ct) =>
            MapResult(await handler.HandleAsync(new SubmitProposalCommand(referenceCode), ct)))
        .RequirePermission(Permissions.ProposalSubmit)
        .RequireIfMatch()
        // §8.2: "required for financially/legally significant transitions: proposal.submit,
        // award.approve, rfq.publish". This is the one the document names first, and the one a
        // double-click actually threatens.
        .RequireIdempotencyKey()
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
        .RequireIfMatch()
        .WithName("WithdrawProposal");

        // T-051, §4.1: UnderReview -> ClarificationRequested. Buyer-side, so rfq.clarify - the same
        // permission the RFQ-level clarification already uses, not a new one.
        group.MapPost("/request-clarification", async (
            string referenceCode,
            WithdrawProposalRequest request,
            IValidator<WithdrawProposalRequest> validator,
            IRequestProposalClarificationHandler handler,
            CancellationToken ct) =>
        {
            // Reuses WithdrawProposalRequest: both carry exactly one mandatory Reason, and its
            // validator already enforces that. A second identical request type would be a second
            // thing to keep in step.
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            return MapResult(await handler.HandleAsync(
                new RequestProposalClarificationCommand(referenceCode, request.Reason), ct));
        })
        .RequirePermission(Permissions.RfqClarify)
        // No RequireIfMatch, deliberately, and this is the Offering lesson from batch 3 applied
        // BEFORE shipping rather than after: a guarded write needs a read that ISSUES its
        // precondition, and a buyer has none for a proposal. GET /proposals/{code} is supplier-scoped
        // - an officer calling it gets a 404 - so an officer literally cannot obtain the ETag this
        // would demand, and every request-clarification would 412 with a message saying the resource
        // changed when nothing had.
        //
        // The write is still safe: RequestClarification refuses any state but UnderReview, so a
        // concurrent second request is a 409 rather than a silent overwrite. Recorded as the reason
        // rather than left as an omission - if a buyer-facing proposal read is ever added, this
        // should take the header with it.
        .WithName("RequestProposalClarification");

        // §4.1: ClarificationRequested -> Revised, supplier_admin / proposal.revise.
        group.MapPost("/revise", async (
            string referenceCode,
            IReviseProposalHandler handler,
            CancellationToken ct) =>
            MapResult(await handler.HandleAsync(new ReviseProposalCommand(referenceCode), ct)))
        .RequirePermission(Permissions.ProposalRevise)
        .RequireIfMatch()
        .WithName("ReviseProposal");
    }
}
