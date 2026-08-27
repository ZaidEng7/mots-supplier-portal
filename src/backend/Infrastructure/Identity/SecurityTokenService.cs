using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Auth;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Identity;

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

        // Single UPDATE ... WHERE ConsumedAt IS NULL - atomic at the DB level, so two concurrent
        // requests presenting the same raw token (a replayed link opened twice) can never both
        // win (STORY-02.2.1 AC2: a used token must reject the second attempt).
        var rowsAffected = await db.SecurityTokens
            .Where(t => t.Id == token.Id && t.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, DateTimeOffset.UtcNow), ct);

        return rowsAffected == 1
            ? new ConsumeSecurityTokenResult.Success(token.UserId)
            : new ConsumeSecurityTokenResult.InvalidOrExpired();
    }
}
