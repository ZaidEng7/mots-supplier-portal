namespace MotsSupplierPortal.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>PEM-encoded RSA private key (PKCS#1 or PKCS#8). SECURITY-ARCHITECTURE.md §1.1
    /// requires RS256 (asymmetric) so workers/services can verify tokens without holding the
    /// signing key. Optional here: when unset, an ephemeral RSA-2048 key is generated per process
    /// (dev-only convenience) - [ASSUMPTION] production must supply a persisted key via secrets/
    /// KMS with the §3.3 JWKS-rotation schedule; a regenerated key on every restart would
    /// invalidate every previously issued token.</summary>
    public string? RsaPrivateKeyPem { get; init; }

    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public int AccessTokenMinutes { get; init; } = 15;
    public int RefreshTokenDays { get; init; } = 30;
}
