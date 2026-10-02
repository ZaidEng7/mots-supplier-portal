// When a refresh token counts as active.
//
// RefreshToken.Active is the rule every session query filters on, the staff list's count, a staff change's
// read-back and a person's own session list, and IsActive is that expression compiled. Each case reads both, so
// the rule is pinned here, faster than the integration suite, and a hand-written IsActive that drifted from the
// expression would fail too.
//
// A token is alive only while it is unrevoked AND unexpired, so each way of being dead is checked against a token
// that is otherwise alive. The revoked token keeps a future expiry, so expiry cannot be what rejects it, and the
// expired token was never revoked, which is what an old sign-in leaves behind.

namespace MotsSupplierPortal.Tests.Unit.Domain;

using FluentAssertions;
using MotsSupplierPortal.Domain.Identity;

public sealed class RefreshTokenTests
{
    private static readonly Func<RefreshToken, bool> ActiveRule = RefreshToken.Active.Compile();

    private static RefreshToken Token(DateTimeOffset expiresAt, DateTimeOffset? revokedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        FamilyId = Guid.NewGuid(),
        TokenHash = "hash",
        CreatedAt = DateTimeOffset.UtcNow.AddHours(-2),
        ExpiresAt = expiresAt,
        RevokedAt = revokedAt,
    };

    [Fact]
    public void An_unrevoked_token_that_has_not_expired_is_active()
    {
        var token = Token(expiresAt: DateTimeOffset.UtcNow.AddDays(7));

        token.IsActive.Should().BeTrue();
        ActiveRule(token).Should().BeTrue();
    }

    [Fact]
    public void A_revoked_token_is_not_active_even_before_it_expires()
    {
        var token = Token(expiresAt: DateTimeOffset.UtcNow.AddDays(7), revokedAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        token.IsActive.Should().BeFalse();
        ActiveRule(token).Should().BeFalse();
    }

    [Fact]
    public void An_expired_token_is_not_active_although_nobody_revoked_it()
    {
        var token = Token(expiresAt: DateTimeOffset.UtcNow.AddHours(-1));

        token.IsActive.Should().BeFalse();
        ActiveRule(token).Should().BeFalse();
    }
}
