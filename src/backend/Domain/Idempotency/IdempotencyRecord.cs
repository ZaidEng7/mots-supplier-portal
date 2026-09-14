// The recorded outcome of one request that must not happen twice, so a retry replays the
// answer instead of doing the work again.
//
// A supplier who presses Submit, loses the connection and presses it again should end up
// with one bid, not two. The client sends a key with the request; this row remembers what
// that key produced.
//
// Keyed by user AND key, never by key alone. The key is chosen by the client, so two
// suppliers can send the same one - unlikely by accident, trivial on purpose. A shared key
// space would let one caller replay another caller's answer, which is far worse than a
// duplicate submission.
//
// RequestFingerprint is what makes the replay safe: a SHA-256 of method, path and body. A
// client that reuses a key for a different request is refused rather than handed the wrong
// answer. It is hashed rather than stored raw because a body can carry a price or a
// rejection reason, and this table is not the place for either.
//
// ResponseStatusCode is null while the request is still running. An in-flight record is
// answered with a refusal rather than a wait, so a slow first attempt cannot hold a second
// one open.
//
// ExpiresAt stores the 24-hour retention rather than computing it, so the cleanup job does
// not need to know the policy and a change to it does not retroactively expire old rows.

namespace MotsSupplierPortal.Domain.Idempotency;

public sealed class IdempotencyRecord
{
    public Guid Id { get; init; }

    public required Guid UserId { get; init; }

    public required string Key { get; init; }

    public required string RequestFingerprint { get; init; }

    public int? ResponseStatusCode { get; set; }

    public string? ResponseBody { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }
}
