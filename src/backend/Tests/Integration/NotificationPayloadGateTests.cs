using FluentAssertions;
using MotsSupplierPortal.Application.Notifications;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// BRULE-091: "No personal or sensitive data in notification payloads."
///
/// <para>The gate is an allow-list, and the reason is the failure it prevents: not a deliberate leak,
/// but someone adding a field for a deep link in six months and putting a price or an email address
/// beside it. A deny-list only catches what somebody already thought of; an allow-list makes adding
/// ANY key a decision.</para>
/// </summary>
public sealed class NotificationPayloadGateTests
{
    [Fact]
    public void A_payload_of_allowed_keys_is_accepted()
    {
        // The control. Without it, a gate that rejected everything would pass the negatives below.
        var json = NotificationPayload.Build(new Dictionary<string, string?>
        {
            ["rfqCode"] = "RFQ-2026-000001",
            ["rfqId"] = Guid.NewGuid().ToString(),
        });

        json.Should().Contain("RFQ-2026-000001");
        NotificationPayload.DisallowedKeysIn(json).Should().BeEmpty();
    }

    [Theory]
    [InlineData("email")]
    [InlineData("supplierEmail")]
    [InlineData("unitPrice")]
    [InlineData("rejectionReason")]
    [InlineData("phone")]
    public void A_payload_carrying_anything_unlisted_is_refused_at_construction(string key)
    {
        // Refused where it is BUILT, not where it is read: a payload that reaches the database has
        // already been written down, and BRULE-091 is about data not being written down.
        var act = () => NotificationPayload.Build(new Dictionary<string, string?> { [key] = "value" });

        act.Should().Throw<InvalidOperationException>().WithMessage("*BRULE-091*");
    }

    [Fact]
    public void A_row_that_somehow_carries_an_unlisted_key_is_detectable_after_the_fact()
    {
        // The second direction of the same rule: data written by an older version, or by hand,
        // is still findable rather than trusted because the writer was supposed to check.
        var disallowed = NotificationPayload.DisallowedKeysIn("""{"rfqCode":"RFQ-1","email":"a@b.co"}""");

        disallowed.Should().ContainSingle().Which.Should().Be("email");
    }

    [Fact]
    public void The_allow_list_holds_only_identifiers_and_routes()
    {
        // Both directions of the list itself. Adding a key must be a deliberate act, and this test is
        // the thing that makes it one: a new key fails here until someone writes it down as approved.
        NotificationPayload.AllowedKeys.Should().BeEquivalentTo(new[]
        {
            "rfqCode", "proposalCode", "supplierCode",
            "rfqId", "proposalId", "awardId", "evaluationId", "notificationId",
            "route",
        }, "every key in a notification payload is either an identifier, a public reference code, or a route - " +
           "anything else is content, and content belongs in the authored copy or behind the link");
    }
}
