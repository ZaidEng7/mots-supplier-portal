using System.Text.Json.Nodes;

namespace MotsSupplierPortal.Application.Proposals;

/// <summary>
/// §12.5's <c>PATCH /proposals/{proposalCode}</c> body, read as RFC 7396 JSON Merge Patch.
///
/// <para><b>Why a JsonObject rather than a DTO.</b> Merge patch draws a distinction a deserialised
/// DTO physically cannot: a member that is ABSENT means "leave this alone", and a member that is
/// explicitly <c>null</c> means "delete this". Both land in a nullable property as null, so a DTO
/// would silently turn "I did not mention my warranty" into "clear my warranty" - on the screen
/// where a supplier prices a public tender. Keeping the parsed JSON lets
/// <see cref="JsonObject.ContainsKey"/> answer the question the type system cannot.</para>
///
/// <para>§4 states the rule normatively - "PATCH | Partial update (JSON Merge Patch, RFC 7396) of
/// draft-editable resources" - and §12.5 works the body: <c>items[]</c>, <c>commercialTerms</c>,
/// <c>technicalResponse</c>.</para>
/// </summary>
public sealed record ProposalMergePatch(JsonObject Body)
{
    public bool Mentions(string member) => Body.ContainsKey(member);

    public JsonNode? Member(string member) => Body.TryGetPropertyValue(member, out var node) ? node : null;

    /// <summary>Present and explicitly null - RFC 7396's delete.</summary>
    public bool Clears(string member) => Mentions(member) && Member(member) is null;
}

/// <summary>The outcome of applying a merge patch, in the vocabulary the endpoint maps to statuses.</summary>
public abstract record ProposalPatchResult
{
    public sealed record Success(ProposalDto Proposal) : ProposalPatchResult;
    public sealed record NotFoundOrNotInvited : ProposalPatchResult;
    public sealed record InvalidState(string Message) : ProposalPatchResult;
    public sealed record Invalid(string Field, string Code, string Detail) : ProposalPatchResult;
}

public interface IPatchProposalHandler
{
    Task<ProposalPatchResult> HandleAsync(string proposalReferenceCode, ProposalMergePatch patch, CancellationToken ct);
}
