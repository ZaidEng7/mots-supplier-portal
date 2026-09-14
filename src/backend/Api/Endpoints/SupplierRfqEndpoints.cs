// Nothing is mapped here any more. Only the request shapes and their validator remain.
//
// The supplier-facing tender routes used to live under their own supplier prefix and now live on the
// single tender collection alongside the buyer's, in the tender endpoints file.
//
// The contract describes the tender list as the supplier-facing list of tenders they were invited to,
// and in the same section documents a buyer-only transition on the same collection, and says of the
// detail response that the visible fields depend on who is asking. So it is one collection, with
// authority decided per caller by permission and row scoping rather than by the shape of the path.
//
// The request shapes stayed because the relocated routes reference them, and moving the types as well
// would have made the change to the routes unreadable.

namespace MotsSupplierPortal.Api.Endpoints;

using FluentValidation;

public sealed record DeclineInvitationRequest(string? Reason);

public sealed record PostClarificationRequest(string Question);

public sealed class PostClarificationRequestValidator : AbstractValidator<PostClarificationRequest>
{
    public PostClarificationRequestValidator() => RuleFor(x => x.Question).NotEmpty().MaximumLength(4000);
}

public static class SupplierRfqEndpoints
{
}
