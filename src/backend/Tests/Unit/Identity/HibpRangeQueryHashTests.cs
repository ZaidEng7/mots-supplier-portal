// Pins the breach-check digest to the algorithm the remote service's protocol requires.
//
// The analyser warning on that call is a false positive, which the call site explains. But a well-meaning
// "upgrade" to a newer hash would compile, pass every other test, and silently disable the breach check: the
// service returns suffixes of the old digest, which can never match a new one, so every password would come back
// clean.
//
// These tests exist so that substitution fails loudly.
//
// The expected values are the service's OWN published documentation example rather than values captured from
// this implementation. A test that asserts whatever the code currently produces would pass just as happily after
// the algorithm changed.
//
// The suffix is what gets compared against the response. If it is wrong, the check reports success for every
// password, including known-breached ones.
//
// And the prefix length is pinned in both directions: the anonymity of the lookup depends on it being short
// enough to be ambiguous, so a longer one would leak the password to the service and a shorter one would change
// the endpoint's path.

namespace MotsSupplierPortal.Tests.Unit.Identity;

using FluentAssertions;
using MotsSupplierPortal.Infrastructure.Identity;

public sealed class HibpRangeQueryHashTests
{
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
        var (_, suffix) = HibpBreachedPasswordValidator.HashForRangeQuery(KnownPassword);

        suffix.Should().Be(KnownSuffix);
    }

    [Fact]
    public void Prefix_is_exactly_five_characters_and_the_two_parts_reconstruct_the_digest()
    {
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
