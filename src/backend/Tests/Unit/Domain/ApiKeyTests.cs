// When an API key may be used, and what revoking one does.
//
// THREE SEPARATE REASONS A KEY IS DEAD and one method that knows all of them. These tests exist because the
// alternative - each caller checking the reasons it remembered - is how a revoked credential goes on working:
// the check that was forgotten is invisible in code that reads perfectly well.
//
// SO EACH REASON IS KILLED ON ITS OWN, with the other two healthy. A test that revoked an expired key would
// pass against an IsUsable that only looked at expiry.
//
// THE LIVE KEY IS THE CONTROL. Without it, an IsUsable that returned false unconditionally would pass every
// other assertion here.
//
// REVOKING TWICE KEEPS THE FIRST REVOCATION'S TIME AND ACTOR, because that is the true one: the second call
// describes somebody clicking a button that had already taken effect. Overwriting would move the audit trail's
// record of when the credential actually stopped working.

namespace MotsSupplierPortal.Tests.Unit.Domain;

using FluentAssertions;
using MotsSupplierPortal.Domain.Integration;

public sealed class ApiKeyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private static ApiKey Key(
        DateTimeOffset? expiresAt = null,
        string[]? permissions = null) => new()
        {
            Id = Guid.CreateVersion7(),
            Name = "Ministry dashboard",
            Prefix = "abcdefgh",
            SecretHash = "hash",
            Salt = "salt",
            Permissions = permissions ?? ["supplier.registry.export"],
            CreatedAt = Now.AddDays(-1),
            ExpiresAt = expiresAt ?? Now.AddDays(365),
        };

    [Fact]
    public void A_live_key_is_usable()
    {
        Key().IsUsable(Now).Should().BeTrue(
            "this is the control - without it every other assertion here would pass against an "
            + "IsUsable that always refused");
    }

    [Fact]
    public void An_expired_key_is_not_usable()
    {
        Key(expiresAt: Now.AddSeconds(-1)).IsUsable(Now).Should().BeFalse();
    }

    [Fact]
    public void A_revoked_key_is_not_usable_although_it_has_not_expired()
    {
        var key = Key();
        key.Revoke(Guid.CreateVersion7(), Now);

        key.IsUsable(Now).Should().BeFalse(
            "revocation has to work on a key that is otherwise perfectly valid, which is the only "
            + "situation anyone ever revokes one in");
    }

    [Fact]
    public void A_key_with_no_permissions_is_not_usable()
    {
        Key(permissions: []).IsUsable(Now).Should().BeFalse(
            "a credential that authenticates and can do nothing is worse than a refusal: it reads as "
            + "a working key to whoever is holding it");
    }

    [Fact]
    public void Revoking_twice_keeps_the_first_revocation()
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        var key = Key();

        key.Revoke(first, Now);
        key.Revoke(second, Now.AddHours(2));

        key.RevokedAt.Should().Be(Now);
        key.RevokedByUserId.Should().Be(first,
            "the first revocation is when the credential actually stopped working, and that is what the "
            + "audit trail has to be able to say");
    }

    [Fact]
    public void Recording_use_stamps_the_time_it_was_used()
    {
        var key = Key();
        key.LastUsedAt.Should().BeNull();

        key.RecordUse(Now);

        key.LastUsedAt.Should().Be(Now);
    }
}
