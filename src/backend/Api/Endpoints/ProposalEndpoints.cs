// The bid routes: starting one, editing it, attaching files, submitting, withdrawing, declining an award, and
// the buyer's clarification request.
//
// Every handler resolves the caller's own bid from their own company, taken from their token. There is no
// route here, or anywhere in this codebase, that can return another supplier's bid.
//
// Permissions follow the written process table exactly. Starting and viewing are open to both of a supplier's
// roles, as is editing a draft. Submitting, withdrawing and declining are the supplier's administrator only.
//
//
// TWO COLLECTIONS, AND WHY
//
// Creating and discovering a bid hang off the tender, because that is where a bid comes from. Everything
// addressing an existing bid is top-level, by the bid's own public code, because the contract addresses a
// bid's sub-resources that way and a resource whose parts are addressable there must be addressable there
// itself.
//
// The read at the code-addressed path is an invention: the contract documents the partial update and the
// submission on that path and no read.
//
//
// ONE PARTIAL UPDATE REPLACING FIVE ROUTES
//
// A single partial update replaces five separate writes: terms, narrative, one line's pricing, deleting a
// line, and answering a requirement.
//
// They are retired outright rather than deprecated. Two ways to edit one resource is how the wrong one
// becomes permanent, and with write preconditions in force they would also be five separate version checks
// over what a supplier experiences as one edit.
//
// The body is read as raw JSON rather than a typed shape, because a merge patch distinguishes a member that
// is absent, meaning leave it alone, from one explicitly set to nothing, meaning delete it, and a
// deserialised shape cannot tell those apart: both arrive as nothing.
//
// Only the merge-patch content type is accepted. Accepting plain JSON would leave the meaning ambiguous at
// exactly the point where absent-versus-empty decides whether a supplier keeps their warranty text.
//
// The retired routes each ran their own validation before touching anything, and the message catalogue is
// keyed by those rules, so retiring the routes must not retire their validation. The same validators run here
// over the same shapes, with each failure re-pathed to where it actually sits in the patch body, because
// those paths exist so the editor can put an error onto the input the user typed in.
//
// Nothing present in the patch being invalid is a success, including when nothing is present at all, which
// the standard makes a legitimate no-op rather than an error.
//
//
// VALIDATION THAT EXISTS BECAUSE OF REAL FAILURES
//
// A unit price must be greater than zero. It was greater than or equal, so a zero-price line was accepted
// while the contract said it could not be. Ruled in favour of the contract, whose own message says so in both
// languages.
//
// Every free-text field on the commercial terms now has a length limit matching its column, and only the
// currency code used to have one. Anything longer reached the database and came back as a raw
// value-too-long error, surfaced to the bidder as an unexpected failure with nothing naming the field or the
// limit. Typing a twelve-character delivery term is enough to do it, which is how this was found while
// filling in a bid.
//
// A failure for a value a person typed is the wrong answer twice over: it tells them the system broke rather
// than that the input was too long, and it puts an unhandled exception in the log for something that is not
// an incident.
//
//
// REFUSALS
//
// An illegal state change answers conflict, naming the current state and what could legally follow. The
// tender routes have answered that way for a while and bids answered a plain bad request, so one product
// carried two conventions for "this resource has moved on".
//
// A refusal with no state attached keeps its plain bad request. Those are shaped like validation, such as a
// missing withdrawal reason, and have no set of allowed next states to offer. The rule governs transitions
// rather than every rejection.
//
// An incomplete submission is unprocessable with a code naming what is missing. The middleware turns the
// identifier the domain threw into the code, so there is no second mapping table.
//
//
// FILES
//
// The supplier declares which envelope a file belongs to. Anything unreadable, absent, or simply not sent
// stays on the commercial side: the parse failing must not be the thing that opens a file up, so the fallback
// is the gated side rather than the last value tried.
//
// A supplier reading their own file has no envelope gate, because the two-envelope rule is about what a
// buyer may see during scoring and not about a bidder reading their own bid. That read is gated on editing
// rather than creating, which both supplier roles hold, and reading a file back is strictly narrower than the
// upload that put it there.
//
//
// DECLINING AN AWARD
//
// It takes a mandatory reason, the same shape and the same length limit as a withdrawal, because both are a
// supplier ending their own participation and both owe an explanation to the record.
//
// No acceptance window is enforced. The written rule tags one as an assumption and names no duration.
//
//
// THE IDEMPOTENCY KEY ON SUBMIT
//
// Submitting requires one. The contract names it first among the financially and legally significant
// transitions, and it is the one a double-click actually threatens.
//
//
// WHY THE BUYER'S CLARIFICATION REQUEST HAS NO WRITE PRECONDITION
//
// This is the lesson from another resource applied before shipping rather than after. A guarded write needs a
// read that issues its precondition, and a buyer has none for a bid: the bid read is supplier-scoped, so an
// officer calling it gets not-found. An officer therefore could not obtain what the guard would demand, and
// every clarification request would be refused with a message saying the resource had changed when nothing
// had.
//
// The write is still safe, because the domain refuses any state but under-review, so a stale request cannot
// silently overwrite anything.
//
// It reuses the withdrawal request shape, because both carry exactly one mandatory reason and its validator
// already enforces that. A second identical shape would be a second thing to keep in step.
//
// It is gated on the same clarification permission the tender-level one uses rather than a new one.
//
//
// PRECONDITION RESPONSES
//
// Every guarded write puts the new version on its own response, so a second write has a precondition to send
// without waiting for a re-read.

namespace MotsSupplierPortal.Api.Endpoints;

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

public sealed record SetItemPricingRequest(decimal Quantity, decimal UnitPrice, decimal? Discount, int? LeadTimeDays, string? NotesAr, string? NotesEn);

public sealed class SetItemPricingRequestValidator : AbstractValidator<SetItemPricingRequest>
{
    public SetItemPricingRequestValidator()
    {
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.UnitPrice).GreaterThan(0);
    }
}

public sealed record SetCommercialTermsRequest(
    string CurrencyCode, string? PaymentTerms, string? IncotermCode,
    string? DeliveryTermsAr, string? DeliveryTermsEn, string? Warranty, DateOnly? ValidityStart, DateOnly? ValidityEnd);

public sealed class SetCommercialTermsRequestValidator : AbstractValidator<SetCommercialTermsRequest>
{
    public SetCommercialTermsRequestValidator()
    {
        RuleFor(x => x.CurrencyCode).NotEmpty().MaximumLength(3);
        RuleFor(x => x.PaymentTerms).MaximumLength(500);
        RuleFor(x => x.IncotermCode).MaximumLength(10);
        RuleFor(x => x.DeliveryTermsAr).MaximumLength(1000);
        RuleFor(x => x.DeliveryTermsEn).MaximumLength(1000);
        RuleFor(x => x.Warranty).MaximumLength(500);
    }
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

public sealed record DeclineAwardOfferRequest(string Reason);

public sealed class DeclineAwardOfferRequestValidator : AbstractValidator<DeclineAwardOfferRequest>
{
    public DeclineAwardOfferRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
}

public static class ProposalEndpoints
{
    private static IResult MapResult(ProposalResult result) => result switch
    {
        ProposalResult.Success s => Results.Ok(s.Proposal),
        ProposalResult.NotFoundOrNotInvited => Results.NotFound(),
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
        var rfqScoped = app.MapGroup("/api/v1/rfqs/{referenceCode}/proposals").WithTags("Proposals");
        var group = app.MapGroup("/api/v1/proposals/{referenceCode}").WithTags("Proposals");

        rfqScoped.MapPost("/", async (string referenceCode, IStartProposalHandler handler, CancellationToken ct) =>
            MapResult(await handler.HandleAsync(referenceCode, ct)))
        .RequirePermission(Permissions.ProposalCreate)
        .WithName("StartProposal");

        app.MapGet("/api/v1/proposals", async (IListMyProposalsHandler handler, CancellationToken ct) =>
        {
            var proposals = await handler.HandleAsync(ct);
            return proposals is null ? Results.NotFound() : Results.Ok(proposals);
        })
        .RequirePermission(Permissions.ProposalCreate)
        .WithTags("Proposals")
        .WithName("ListMyProposals");

        rfqScoped.MapGet("/", async (string referenceCode, IGetProposalHandler handler, CancellationToken ct) =>
            MapResult(await handler.HandleAsync(referenceCode, ct)))
        .RequirePermission(Permissions.ProposalCreate)
        .WithETag()
        .WithName("GetProposal");

        group.MapGet("/", async (string referenceCode, IGetProposalByCodeHandler handler, CancellationToken ct) =>
            MapResult(await handler.HandleAsync(referenceCode, ct)))
        .RequirePermission(Permissions.ProposalCreate)
        .WithETag()
        .WithName("GetProposalByCode");

        group.MapPatch("/", async (
            string referenceCode,
            HttpContext http,
            IPatchProposalHandler handler,
            IValidator<SetItemPricingRequest> itemValidator,
            IValidator<SetCommercialTermsRequest> termsValidator,
            IValidator<AnswerRequirementRequest> answerValidator,
            CancellationToken ct) =>
        {
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

            var invalid = await ValidatePatchAsync(patch, itemValidator, termsValidator, answerValidator, ct);
            if (invalid is not null) return invalid;

            return MapPatchResult(await handler.HandleAsync(referenceCode, new ProposalMergePatch(patch), ct));
        })
        .RequirePermission(Permissions.ProposalEdit)
        .RequireIfMatch()
        .WithETag()
        .WithFreshETag()
.WithName("PatchProposal");

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

            var envelope = Enum.TryParse<ProposalDocumentEnvelope>(form["envelope"].ToString(), ignoreCase: true, out var parsed)
                ? parsed
                : ProposalDocumentEnvelope.Commercial;
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

        group.MapGet("/documents/{documentId:guid}/download-url", async (
            string referenceCode, Guid documentId,
            IGetOwnProposalDocumentDownloadUrlHandler handler, CancellationToken ct) =>
            await handler.HandleAsync(referenceCode, documentId, ct) switch
            {
                ProposalDocumentDownloadResult.Success s => Results.Ok(new { url = s.Url, fileName = s.FileName }),
                _ => Results.NotFound(),
            })
        .RequirePermission(Permissions.ProposalEdit)
        .WithName("GetOwnProposalDocumentDownloadUrl");

        group.MapDelete("/documents/{documentId:guid}", async (
            string referenceCode, Guid documentId, IManageProposalDocumentHandler handler, CancellationToken ct) =>
            MapResult(await handler.RemoveAsync(new RemoveProposalDocumentCommand(referenceCode, documentId), ct)))
        .RequirePermission(Permissions.ProposalEdit)
        .WithName("RemoveProposalDocument");

        group.MapPost("/decline", async (
            string referenceCode, DeclineAwardOfferRequest request,
            IDeclineAwardOfferHandler handler, CancellationToken ct) =>
        {
            return MapResult(await handler.HandleAsync(
                new DeclineAwardOfferCommand(referenceCode, request.Reason), ct));
        })
        .RequirePermission(Permissions.ProposalDecline)
        .RequireIfMatch()
        .Validate<DeclineAwardOfferRequest>()
        .WithETag()
        .WithFreshETag()
.WithName("DeclineAwardOffer");

        group.MapPost("/submit", async (string referenceCode, ISubmitProposalHandler handler, CancellationToken ct) =>
            MapResult(await handler.HandleAsync(new SubmitProposalCommand(referenceCode), ct)))
        .RequirePermission(Permissions.ProposalSubmit)
        .RequireIfMatch()
        .RequireIdempotencyKey()
        .WithFreshETag()
.WithName("SubmitProposal");

        group.MapPost("/withdraw", async (
            string referenceCode,
            WithdrawProposalRequest request,
            IWithdrawProposalHandler handler,
            CancellationToken ct) =>
        {
            return MapResult(await handler.HandleAsync(new WithdrawProposalCommand(referenceCode, request.Reason), ct));
        })
        .RequirePermission(Permissions.ProposalWithdraw)
        .RequireIfMatch()
        .Validate<WithdrawProposalRequest>()
        .WithFreshETag()
.WithName("WithdrawProposal");

        group.MapPost("/request-clarification", async (
            string referenceCode,
            WithdrawProposalRequest request,
            IRequestProposalClarificationHandler handler,
            CancellationToken ct) =>
        {
            return MapResult(await handler.HandleAsync(
                new RequestProposalClarificationCommand(referenceCode, request.Reason), ct));
        })
        .RequirePermission(Permissions.RfqClarify)
        .Validate<WithdrawProposalRequest>()
        .WithName("RequestProposalClarification");

        group.MapPost("/revise", async (
            string referenceCode,
            IReviseProposalHandler handler,
            CancellationToken ct) =>
            MapResult(await handler.HandleAsync(new ReviseProposalCommand(referenceCode), ct)))
        .RequirePermission(Permissions.ProposalRevise)
        .RequireIfMatch()
        .WithFreshETag()
.WithName("ReviseProposal");
    }
}
