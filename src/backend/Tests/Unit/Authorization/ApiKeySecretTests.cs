// The secret half of an API key: what it generates, and what it accepts.
//
// THE CONTROL IS THE FIRST TEST. A verifier that returned true unconditionally would pass every "the right
// secret works" assertion ever written, so the right secret working is only worth asserting beside a wrong one
// failing - and beside a right secret checked against another key's salt failing, which is the mistake a
// copy-paste in the handler would produce and which a single-key test cannot see.
//
// THE FORMAT IS PARSED BACK because the handler has to split a header into a prefix it can look up and a secret
// it can hash. Every shape that is not one this product issues has to be refused rather than half-read: a
// truncated key that parsed into a prefix and an empty secret would be verified against a hash, which is a
// comparison nobody meant to make.
//
// TWO KEYS NEVER COLLIDE is asserted on the prefix rather than the secret, because the prefix is the shorter of
// the two and carries a unique index in the database. Eight base64url characters is 48 bits; the point of the
// assertion is not the arithmetic but that the generator is actually random per call rather than seeded once.

namespace MotsSupplierPortal.Tests.Unit.Authorization;

using FluentAssertions;
using MotsSupplierPortal.Domain.Integration;
using MotsSupplierPortal.Infrastructure.Integration;

public sealed class ApiKeySecretTests
{
    [Fact]
    public void A_generated_secret_verifies_against_its_own_hash_and_a_wrong_one_does_not()
    {
        var generated = ApiKeySecret.Generate();
        ApiKeySecret.TryParse(generated.Presented, out _, out var secret).Should().BeTrue();

        ApiKeySecret.Verify(secret, generated.Salt, generated.SecretHash).Should().BeTrue();
        ApiKeySecret.Verify($"{secret}x", generated.Salt, generated.SecretHash).Should().BeFalse(
            "a verifier that accepted anything would pass every other assertion in this file");
    }

    [Fact]
    public void A_secret_checked_against_another_keys_salt_does_not_verify()
    {
        var first = ApiKeySecret.Generate();
        var second = ApiKeySecret.Generate();
        ApiKeySecret.TryParse(first.Presented, out _, out var secret).Should().BeTrue();

        ApiKeySecret.Verify(secret, second.Salt, first.SecretHash).Should().BeFalse(
            "the salt is part of the hash, so pairing a secret with the wrong key's salt must fail - "
            + "a handler that read the salt from the wrong row would otherwise go unnoticed");
    }

    [Fact]
    public void The_presented_form_carries_the_prefix_the_key_is_looked_up_by()
    {
        var generated = ApiKeySecret.Generate();

        ApiKeySecret.TryParse(generated.Presented, out var prefix, out var secret).Should().BeTrue();

        prefix.Should().Be(generated.Prefix);
        prefix.Should().HaveLength(ApiKey.PrefixLength);
        secret.Should().NotBeEmpty();
        generated.Presented.Should().StartWith(ApiKeySecret.PresentedPrefix);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("mots_short_")]
    [InlineData("mots_abcdefgh")]
    [InlineData("mots_abcdefgh_")]
    [InlineData("Bearer eyJhbGciOiJSUzI1NiJ9")]
    public void A_shape_this_product_does_not_issue_is_refused(string presented)
    {
        ApiKeySecret.TryParse(presented, out _, out _).Should().BeFalse(
            $"'{presented}' is not a key this product issues, and half-reading it would mean hashing "
            + "whatever was left against a stored hash");
    }

    [Fact]
    public void Two_generated_keys_do_not_share_a_prefix()
    {
        var prefixes = Enumerable.Range(0, 50).Select(_ => ApiKeySecret.Generate().Prefix).ToList();

        prefixes.Distinct().Should().HaveCount(prefixes.Count,
            "the prefix carries a unique index, so a generator that repeated itself would fail at insert");
    }
}
