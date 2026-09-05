namespace MotsSupplierPortal.Domain.Idempotency;

/// <summary>
/// T-053/§8.2: one recorded outcome of a non-idempotent POST, so a retry replays it instead of doing
/// the work twice.
///
/// <para>§8.2 names the fields: <c>{key, requestFingerprint(hash of method+path+body), userId,
/// responseSnapshot}</c>, retained 24 hours and garbage-collected by Hangfire.</para>
///
/// <para><b>Keyed by (UserId, Key), not by Key alone.</b> The key is client-generated, so two
/// suppliers can pick the same UUID - astronomically unlikely by accident and trivial on purpose. A
/// global key space would let one caller replay another caller's response, which is a far worse
/// failure than a duplicate submission.</para>
///
/// <para><b>The fingerprint is what makes replay safe.</b> Without it, a client reusing a key for a
/// different request would be handed the wrong answer; §8.2 requires that case to be a 409 instead.
/// The fingerprint covers method, path and body, so the same key against a different endpoint is also
/// caught.</para>
/// </summary>
public sealed class IdempotencyRecord
{
    public Guid Id { get; init; }

    public required Guid UserId { get; init; }

    public required string Key { get; init; }

    /// <summary>SHA-256 of <c>METHOD\nPATH\nBODY</c>, hex. Hashed rather than stored raw: a request
    /// body can carry a price or a rejection reason, and this table is not the place for either.</summary>
    public required string RequestFingerprint { get; init; }

    /// <summary>Set once the handler has answered. Null means the request is IN FLIGHT - see
    /// IdempotencyStore on why an in-flight record is a 409 and not a wait.</summary>
    public int? ResponseStatusCode { get; set; }

    public string? ResponseBody { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>§8.2's 24-hour retention, stored rather than computed so the GC job does not have to
    /// know the policy and a change to it does not retroactively expire old rows.</summary>
    public DateTimeOffset ExpiresAt { get; init; }
}
