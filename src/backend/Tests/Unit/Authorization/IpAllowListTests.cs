// Whether a key's address list lets a caller through.
//
// THE CONTROL IS THE EMPTY LIST, and it is also the case that matters most today: no key has a list yet,
// because nobody knows the address of the server that will run the ministry's nightly load. A change that made
// an empty list mean "nothing allowed" would refuse every key in existence, and would do it the moment it
// shipped rather than when somebody first configured a list.
//
// THE NEAR MISSES ARE THE POINT OF THE REST. 10.42.0.99 against a list holding 10.42.0.9 is what a string
// comparison lets through, and it is the mistake this is most likely to be written as. A /24 that accepts a
// neighbour in the next network is the same mistake with arithmetic instead of text.
//
// THE MAPPED ADDRESS IS NOT AN EDGE CASE. A dual-stack server reports an old-style caller as a new-style
// address wrapping it, so a list written the way everyone writes lists would match nothing at all on exactly
// the deployments most likely to run this.
//
// A MALFORMED ENTRY MUST FAIL CLOSED. The validator refuses one at creation, so a bad entry in the database
// means something else has gone wrong - and at that point refusing the request is the only safe reading, since
// the alternative is a key whose allow-list silently allows everyone.

namespace MotsSupplierPortal.Tests.Unit.Authorization;

using System.Net;
using FluentAssertions;
using MotsSupplierPortal.Infrastructure.Integration;

public sealed class IpAllowListTests
{
    private static IPAddress Address(string value) => IPAddress.Parse(value);

    [Fact]
    public void A_key_with_no_list_is_usable_from_anywhere()
    {
        IpAllowList.Allows([], Address("203.0.113.7")).Should().BeTrue(
            "no key carries a list yet, so this is every key in existence");
    }

    [Fact]
    public void A_key_with_a_list_refuses_a_caller_whose_address_is_unknown()
    {
        IpAllowList.Allows(["10.42.0.9"], caller: null).Should().BeFalse(
            "an address the server could not determine cannot be shown to be on the list");
    }

    [Theory]
    [InlineData("10.42.0.9", "10.42.0.9", true)]
    [InlineData("10.42.0.9", "10.42.0.99", false)]
    [InlineData("10.42.0.9", "10.42.0.1", false)]
    [InlineData("10.42.0.0/24", "10.42.0.99", true)]
    [InlineData("10.42.0.0/24", "10.42.1.1", false)]
    [InlineData("10.42.0.0/16", "10.42.200.5", true)]
    [InlineData("10.42.0.0/32", "10.42.0.0", true)]
    [InlineData("0.0.0.0/0", "203.0.113.7", true)]
    public void An_address_is_matched_by_its_bytes_rather_than_its_text(string range, string caller, bool allowed)
    {
        IpAllowList.Allows([range], Address(caller)).Should().Be(allowed,
            $"'{caller}' against '{range}' - a prefix comparison on text would let 10.42.0.99 pass a list "
            + "holding 10.42.0.9");
    }

    [Fact]
    public void A_caller_matching_any_entry_is_allowed()
    {
        string[] ranges = ["192.0.2.0/24", "10.42.0.0/24", "203.0.113.7"];

        IpAllowList.Allows(ranges, Address("10.42.0.5")).Should().BeTrue();
        IpAllowList.Allows(ranges, Address("203.0.113.7")).Should().BeTrue();
        IpAllowList.Allows(ranges, Address("198.51.100.1")).Should().BeFalse();
    }

    [Fact]
    public void An_old_style_caller_wrapped_by_a_dual_stack_server_still_matches_an_old_style_list()
    {
        IpAllowList.Allows(["10.42.0.0/24"], Address("::ffff:10.42.0.9")).Should().BeTrue(
            "this is how a dual-stack server reports an ordinary caller, and a list nobody can write "
            + "in that form would match nothing on exactly those deployments");
    }

    [Theory]
    [InlineData("2001:db8::/32", "2001:db8:1234::1", true)]
    [InlineData("2001:db8::/32", "2001:db9::1", false)]
    public void New_style_addresses_are_matched_too(string range, string caller, bool allowed)
    {
        IpAllowList.Allows([range], Address(caller)).Should().Be(allowed);
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("10.42.0.0/33")]
    [InlineData("10.42.0.0/-1")]
    [InlineData("10.42.0.0/")]
    [InlineData("")]
    public void An_entry_that_cannot_be_read_matches_nothing(string entry)
    {
        IpAllowList.Allows([entry], Address("10.42.0.9")).Should().BeFalse(
            $"'{entry}' is not a range, and the alternative reading - allowing everyone - is an open door");
        IpAllowList.IsValidEntry(entry).Should().BeFalse();
    }

    [Theory]
    [InlineData("10.42.0.9")]
    [InlineData("10.42.0.0/24")]
    [InlineData("2001:db8::/32")]
    [InlineData(" 10.42.0.9 ")]
    public void A_real_range_is_accepted_by_the_validator(string entry)
    {
        IpAllowList.IsValidEntry(entry).Should().BeTrue();
    }
}
