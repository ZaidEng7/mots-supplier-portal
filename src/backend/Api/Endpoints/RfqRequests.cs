// What a caller may send to a tender route, and what counts as valid.
//
// One record per request shape, each with the checks that apply to it. They live beside each other
// rather than beside the routes, so the routes read as a list of routes.
//
// Moving a deadline takes the new deadline itself rather than a duration, because a duration needs an
// anchor and the route would have to pick one. It also takes a mandatory reason. There is no cap on an
// extension, because a cap would invent a fairness rule; a required reason makes every extension
// defensible or obviously indefensible without inventing one, and a supplier being told why their
// deadline moved is simply better than being told that it did.
//
// That request deliberately has no must-be-in-the-future check. The domain owns that rule, and
// duplicating it here would give two answers to the same question the day one of them changed.
//
// A requirement's expected envelope is optional and only meaningful when the requirement asks for a
// document at all.
//
// Reassigning a tender takes a mandatory reason, because the audit row is the operation's whole purpose.
//
// Submitting for review may name the manager the pass is waiting on, and the whole body is optional, so
// every existing caller that posted nothing keeps working. Naming an approver is an addition to that
// transition rather than a new requirement of it, and there is no routing rule that would let the server
// fill it in.
//
// Answering a clarification no longer takes a publish flag. Answering publishes to every invitee, so a
// flag whose only remaining legal value is true would be a lie the caller could tell.
//
// Length limits here match the column widths behind them. That is not decoration: a value longer than
// its column reaches the database and comes back as a raw constraint error, which a caller sees as an
// unexpected failure with nothing naming the field or the limit.

namespace MotsSupplierPortal.Api.Endpoints;

using FluentValidation;

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

public sealed record ChangeSubmissionDeadlineRequest(DateTimeOffset SubmissionDeadline, string Reason);

public sealed class ChangeSubmissionDeadlineRequestValidator : AbstractValidator<ChangeSubmissionDeadlineRequest>
{
    public ChangeSubmissionDeadlineRequestValidator()
    {
        RuleFor(x => x.SubmissionDeadline).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
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

public sealed record RequirementRequest(
    string TextAr, string TextEn, bool IsMandatory, string? DocumentTypeCode,
    MotsSupplierPortal.Domain.Proposals.ProposalDocumentEnvelope? ExpectedEnvelope = null);

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

public sealed record ReassignRfqRequest(Guid NewOwnerUserId, string Reason);

public sealed class ReassignRfqRequestValidator : AbstractValidator<ReassignRfqRequest>
{
    public ReassignRfqRequestValidator()
    {
        RuleFor(x => x.NewOwnerUserId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
    }
}

public sealed record SubmitForReviewRequest(Guid? AssignedApproverUserId);

public sealed record CloseSubmissionRequest(string? Reason);

public sealed record CancelRfqRequest(string Reason);

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

public sealed record AnswerClarificationRequest(string Answer);

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
