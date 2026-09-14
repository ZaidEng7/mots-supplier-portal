// The partial-update body for a bid, read as a merge patch.
//
//
// WHY IT IS RAW JSON AND NOT A TYPED SHAPE
//
// A merge patch draws a distinction a deserialised shape physically cannot. A member that is absent means
// leave this alone. A member explicitly set to nothing means delete this.
//
// Both arrive in an optional property as nothing, so a typed shape would silently turn "I did not mention my
// warranty" into "clear my warranty", on the screen where a supplier prices a public tender.
//
// Keeping the parsed JSON lets the code ask whether a key was present at all, which is the question the type
// system cannot answer.

namespace MotsSupplierPortal.Application.Proposals;

using System.Text.Json.Nodes;

public sealed record ProposalMergePatch(JsonObject Body)
{
    public bool Mentions(string member) => Body.ContainsKey(member);

    public JsonNode? Member(string member) => Body.TryGetPropertyValue(member, out var node) ? node : null;

    public bool Clears(string member) => Mentions(member) && Member(member) is null;
}

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
