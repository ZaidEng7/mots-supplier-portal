// Audit rows record the caller's address truncated to its network rather than in full.
//
// The decision and its reasoning live at the call site. These tests pin the behaviour, so a later
// "simplification" to storing the full address is a failing test rather than a silent privacy regression in a
// table retained indefinitely.
//
// A dual-stack server reports callers arriving over the older protocol in a mapped form. Taking the newer
// protocol's branch there would mask the wrong bytes and store a value that is neither the right network nor
// obviously wrong, which is the worst of both.
//
// And two addresses on one network must be indistinguishable afterwards. If they are not, the truncation is
// decorative and the privacy argument for it does not hold.

namespace MotsSupplierPortal.Tests.Unit.Audit;

using System.Net;
using FluentAssertions;
using MotsSupplierPortal.Api.Authorization;

public sealed class IpTruncationTests
{
    [Theory]
    [InlineData("203.0.113.42", "203.0.113.0/24")]
    [InlineData("10.1.2.255", "10.1.2.0/24")]
    [InlineData("192.168.0.1", "192.168.0.0/24")]
    public void IPv4_is_truncated_to_a_24(string input, string expected) =>
        HttpAuditContext.Truncate(IPAddress.Parse(input)).Should().Be(expected);

    [Theory]
    [InlineData("2001:db8:1234:5678::1", "2001:db8:1234::/48")]
    [InlineData("2001:db8:1234:ffff:ffff:ffff:ffff:ffff", "2001:db8:1234::/48")]
    public void IPv6_is_truncated_to_a_48(string input, string expected) =>
        HttpAuditContext.Truncate(IPAddress.Parse(input)).Should().Be(expected);

    [Fact]
    public void IPv4_mapped_IPv6_is_unmapped_before_truncation()
    {
        HttpAuditContext.Truncate(IPAddress.Parse("::ffff:203.0.113.5"))
            .Should().Be("203.0.113.0/24");
    }

    [Fact]
    public void Truncation_discards_the_host_portion_rather_than_masking_it_in_place()
    {
        HttpAuditContext.Truncate(IPAddress.Parse("203.0.113.7"))
            .Should().Be(HttpAuditContext.Truncate(IPAddress.Parse("203.0.113.200")));
    }
}
