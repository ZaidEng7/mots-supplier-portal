// Issuing and consuming the opaque single-use tokens behind verification, password reset and invitations.
//
// Only the hash is stored, never the token, so a stolen database yields nothing that can be presented.
//
// Consuming is a single conditional update against the not-yet-consumed row, which is atomic at the database.
// So two concurrent requests presenting the same token, a link opened twice, can never both win, and the second
// attempt is refused.

namespace MotsSupplierPortal.Infrastructure.Identity;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Auth;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class SecurityTokenService(AppDbContext db) : ISecurityTokenService
{
    public async Task<string> IssueAsync(Guid userId, SecurityTokenPurpose purpose, TimeSpan ttl, CancellationToken ct)
    {
        var raw = TokenHasher.GenerateOpaqueToken();
        db.SecurityTokens.Add(new SecurityToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            TokenHash = TokenHasher.Hash(raw),
            Purpose = purpose,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.Add(ttl),
        });
        await db.SaveChangesAsync(ct);
        return raw;
    }

    public async Task<ConsumeSecurityTokenResult> ConsumeAsync(string rawToken, SecurityTokenPurpose purpose, CancellationToken ct)
    {
        var hash = TokenHasher.Hash(rawToken);

        var token = await db.SecurityTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.Purpose == purpose, ct);
        if (token is null || !token.IsValid)
        {
            return new ConsumeSecurityTokenResult.InvalidOrExpired();
        }

        var rowsAffected = await db.SecurityTokens
            .Where(t => t.Id == token.Id && t.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, DateTimeOffset.UtcNow), ct);

        return rowsAffected == 1
            ? new ConsumeSecurityTokenResult.Success(token.UserId)
            : new ConsumeSecurityTokenResult.InvalidOrExpired();
    }
}
