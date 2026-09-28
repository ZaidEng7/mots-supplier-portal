// Encrypting a credential that has to live in the database.
//
// THE ROUND TRIP IS THE OBVIOUS TEST AND THE LEAST INTERESTING ONE. The two that matter are that the same
// plaintext encrypts differently every time - a fresh nonce, which is the one thing that must never repeat under
// GCM - and that altering a stored value makes it fail to decrypt rather than decrypt into something else.
// "Something else" sent as a credential to another ministry's server is not a failure mode worth having.
//
// THE MISSING KEY THROWS AND NAMES THE SETTING, because the helpful alternative - storing the secret unencrypted
// when no key is configured - turns one configuration mistake into a database of plain-text credentials nobody
// knows about.

namespace MotsSupplierPortal.Tests.Unit.Integration;

using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using MotsSupplierPortal.Infrastructure.Integration;

public sealed class SecretCipherTests
{
    private static SecretCipher With(string? key)
    {
        var settings = new Dictionary<string, string?> { [SecretCipher.KeySetting] = key };

        return new SecretCipher(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
    }

    private static string GoodKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void A_secret_survives_the_round_trip()
    {
        var cipher = With(GoodKey());

        cipher.Unprotect(cipher.Protect("token abc:def")).Should().Be("token abc:def");
    }

    [Fact]
    public void The_same_secret_encrypts_differently_every_time()
    {
        var cipher = With(GoodKey());

        cipher.Protect("same").Should().NotBe(
            cipher.Protect("same"),
            "a nonce reused with one key breaks GCM completely, so it must be fresh on every call");
    }

    [Fact]
    public void A_tampered_value_refuses_to_decrypt_rather_than_decrypting_into_something_else()
    {
        var cipher = With(GoodKey());
        var parts = cipher.Protect("token abc:def").Split('.');
        var body = Convert.FromBase64String(parts[1]);
        body[0] ^= 0xFF;

        var tampered = $"{parts[0]}.{Convert.ToBase64String(body)}.{parts[2]}";

        var act = () => cipher.Unprotect(tampered);

        act.Should().Throw<CryptographicException>(
            "a credential that decrypts into something else is worse than one that fails");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void With_no_key_configured_it_refuses_rather_than_storing_plain_text(string? key)
    {
        var act = () => With(key).Protect("secret");

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{SecretCipher.KeySetting}*");
    }

    [Fact]
    public void A_key_that_is_not_base64_is_refused_by_name()
    {
        var act = () => With("not base64 at all !!").Protect("secret");

        act.Should().Throw<InvalidOperationException>().WithMessage("*valid base64*");
    }

    [Fact]
    public void A_key_of_the_wrong_length_is_refused_and_says_what_it_got()
    {
        var act = () => With(Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))).Protect("secret");

        act.Should().Throw<InvalidOperationException>().WithMessage("*32 bytes, not 16*");
    }
}
