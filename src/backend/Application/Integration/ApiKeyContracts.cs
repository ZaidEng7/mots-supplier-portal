// What the product can be asked to do with an API key: issue one, list them, revoke one.
//
// THERE IS NO EDIT AND NO RE-READ, and both absences are the design. A key's permissions and expiry cannot be
// changed after issue, because a credential whose reach can be widened in place is one whose reach nobody can
// reason about from the audit trail; you issue a new key instead. And the secret is returned exactly once, by
// the create call, because nothing stores it - a "show me the key again" operation could only exist if the
// product kept a copy.
//
// ROTATION IS THEREFORE NOT AN OPERATION EITHER. It is: issue the second key, let both work while the caller
// switches, revoke the first. That only stays true if issuing is cheap, which is why there is a screen for it
// rather than a database script.
//
// THE SUMMARY CARRIES THE PREFIX AND NEVER THE SECRET. The prefix is what appears on screen, in the audit trail
// and in logs; it names a key without being usable as one.
//
// LASTUSEDAT IS ON THE SUMMARY because it answers the only question that makes revoking safe: whether anything
// is still calling with this key. A list without it invites either revoking something in use or leaving
// something unaccounted for in place.

namespace MotsSupplierPortal.Application.Integration;

public sealed record CreateApiKeyCommand(string Name, int? LifetimeDays);

public sealed record RevokeApiKeyCommand(Guid ApiKeyId);

public sealed record ApiKeySummary(
    Guid Id,
    string Name,
    string Prefix,
    IReadOnlyList<string> Permissions,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt);

public sealed record CreatedApiKey(ApiKeySummary Key, string Secret);

public abstract record ApiKeyMutationResult
{
    public sealed record Created(CreatedApiKey Key) : ApiKeyMutationResult;

    public sealed record Revoked(ApiKeySummary Key) : ApiKeyMutationResult;

    public sealed record NotFound : ApiKeyMutationResult;
}

public interface ICreateApiKeyHandler
{
    Task<ApiKeyMutationResult> HandleAsync(CreateApiKeyCommand command, CancellationToken ct);
}

public interface IListApiKeysHandler
{
    Task<IReadOnlyList<ApiKeySummary>> HandleAsync(CancellationToken ct);
}

public interface IRevokeApiKeyHandler
{
    Task<ApiKeyMutationResult> HandleAsync(RevokeApiKeyCommand command, CancellationToken ct);
}
