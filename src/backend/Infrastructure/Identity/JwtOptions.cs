// The access token's configuration: the signing key, who issues it, who it is for, and how long the two tokens
// last.
//
// The signing key is asymmetric, because the written security architecture requires it so that workers and
// services can verify a token without holding the key that signs it.
//
// It is optional here. With no key configured, an ephemeral one is generated per process, which is a
// development convenience.
//
// That is recorded as an assumption rather than a decision: production must supply a persisted key through
// secrets management, on the rotation schedule the architecture names, because a regenerated key on every
// restart invalidates every token already issued.

namespace MotsSupplierPortal.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string? RsaPrivateKeyPem { get; init; }

    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public int AccessTokenMinutes { get; init; } = 15;
    public int RefreshTokenDays { get; init; } = 30;
}
