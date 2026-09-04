namespace MotsSupplierPortal.Domain.Proposals;

/// <summary>
/// T-066: a submission refused because the proposal is INCOMPLETE, as distinct from one refused
/// because of its state or the RFQ's window.
///
/// <para>§12.5 answers the first with <c>422 (PROPOSAL_ITEMS_REQUIRED)</c> and the second with 409
/// or 400, and the two had shared one 400. The distinction is not cosmetic: a 409 tells a supplier
/// their proposal has moved on and there is nothing to fix, while a 422 tells them exactly what to
/// go and fill in. Telling a supplier with an unpriced item that their proposal had moved on is the
/// failure batch 8 deliberately avoided when it left these as 400s rather than sweeping them into
/// the new 409.</para>
///
/// <para><paramref name="Error"/> is a lower_snake identifier, because that is what
/// ProblemDetailsMiddleware turns into §7's SCREAMING_SNAKE <c>code</c>. Only
/// <c>proposal_items_required</c> is named by §12.5; the other two are inventions and are marked as
/// such where they are thrown.</para>
/// </summary>
public sealed class ProposalIncompleteException(string error, string message) : Exception(message)
{
    public string Error { get; } = error;
}
