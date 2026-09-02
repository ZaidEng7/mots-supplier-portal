using FluentValidation;

namespace MotsSupplierPortal.Api.Endpoints;

public sealed record DeclineInvitationRequest(string? Reason);

public sealed record PostClarificationRequest(string Question);

public sealed class PostClarificationRequestValidator : AbstractValidator<PostClarificationRequest>
{
    public PostClarificationRequestValidator() => RuleFor(x => x.Question).NotEmpty().MaximumLength(4000);
}

/// <summary>
/// §12-A/C1: the supplier-facing RFQ routes that used to live at
/// <c>/api/v1/suppliers/me/rfqs/**</c> now live on the single <c>/api/v1/rfqs</c> collection, in
/// <see cref="RfqEndpoints"/>.
///
/// <para>API-ARCHITECTURE.md §12.4 documents <c>GET /rfqs</c> as *"supplier-facing list of
/// invited/published RFQs"* while documenting <c>POST /rfqs/{rfqCode}/publish</c> in the same
/// section as a buyer transition, and states of the detail response that *"Fields visible per
/// persona are row-scoped"* with *"- for buyers - invitations[]"*. One collection, authority
/// decided per caller by permission and row-scope (§9.2), not by path prefix.</para>
///
/// <para>Only the request contracts and their validator remain here, because they are referenced
/// by the relocated endpoints and moving the types too would have made the route diff unreadable.
/// The class itself no longer maps anything.</para>
/// </summary>
public static class SupplierRfqEndpoints
{
}
