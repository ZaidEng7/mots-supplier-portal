using FluentAssertions;
using MotsSupplierPortal.Infrastructure.Identity;

namespace MotsSupplierPortal.Tests.Unit.Identity;

/// <summary>
/// Pins the HIBP range-query digest to SHA-1.
///
/// The S4790 warning on that SHA-1 call is a false positive (see the note at the call site), but a
/// well-meaning "fix" to SHA-256 would compile, pass every other test, and silently disable the
/// breach check: the API returns SHA-1 suffixes, which can never match a SHA-256 one, so every
/// password would come back clean. These tests exist so that substitution fails loudly.
///
/// The expected values are HIBP's own published documentation vector, not values captured from this
/// implementation - a test that asserts whatever the code currently produces would pass just as
/// happily after the algorithm changed.
/// </summary>
public sealed class HibpRangeQueryHashTests
{
    // SHA-1("password") = 5BAA61E4C9B93F3F0682250B6CF8331B7EE68FD8. This is the example HIBP uses in
    // its own range-API documentation, and the most-breached password on record.
    private const string KnownPassword = "password";
    private const string KnownPrefix = "5BAA6";
    private const string KnownSuffix = "1E4C9B93F3F0682250B6CF8331B7EE68FD8";

    [Fact]
    public void Prefix_matches_the_published_HIBP_vector()
    {
        var (prefix, _) = HibpBreachedPasswordValidator.HashForRangeQuery(KnownPassword);

        prefix.Should().Be(KnownPrefix,
            "the HIBP range API is keyed on SHA-1; any other algorithm silently matches nothing");
    }

    [Fact]
    public void Suffix_matches_the_published_HIBP_vector()
    {
        // The suffix is what gets compared against the API response. If it is wrong, the check
        // returns Success for every password including known-breached ones.
        var (_, suffix) = HibpBreachedPasswordValidator.HashForRangeQuery(KnownPassword);

        suffix.Should().Be(KnownSuffix);
    }

    [Fact]
    public void Prefix_is_exactly_five_characters_and_the_two_parts_reconstruct_the_digest()
    {
        // k-anonymity depends on the prefix being short enough to be ambiguous. A longer prefix
        // would leak the password to HIBP; a shorter one would change the endpoint path.
        var (prefix, suffix) = HibpBreachedPasswordValidator.HashForRangeQuery(KnownPassword);

        prefix.Should().HaveLength(5);
        (prefix + suffix).Should().HaveLength(40, "a SHA-1 digest is 40 hex characters");
    }

    [Fact]
    public void Digest_is_uppercase_hex_because_the_API_response_is_compared_case_sensitively_upstream()
    {
        var (prefix, suffix) = HibpBreachedPasswordValidator.HashForRangeQuery(KnownPassword);

        (prefix + suffix).Should().MatchRegex("^[0-9A-F]{40}$");
    }
}
