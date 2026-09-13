// The supplier's missing-fields list and the codes a reviewer may flag are ONE vocabulary.
//
// That list feeds the profile read model, which the interface compares by string equality against the same
// vocabulary the flagging codes define. The interface's own comment names this dependency and asks the reader to
// keep the two in step.
//
// Before this, that step existed only because two independent string literals happened to agree. Nothing would
// have caught a rename silently orphaning a missing-field indicator.
//
// This test would have failed against the older code the moment a maintainer renamed one of the constants
// without also updating the matching literal, which is exactly the failure the ticket describes. Now it cannot
// happen at all: the values ARE the same constant, so a rename is a compile error in the aggregate itself rather
// than a silent divergence this test would be the only thing to catch.
//
// The denominator is stated rather than assumed: registration already sets the legal information from the display
// names, so a freshly registered supplier is missing everything except that, which is five items.

namespace MotsSupplierPortal.Tests.Unit.Domain;

using FluentAssertions;
using MotsSupplierPortal.Domain.Suppliers;

public sealed class GetMissingProfileFieldsVocabularyTests
{
    [Fact]
    public void Every_missing_field_code_except_termsAccepted_is_a_known_ProfileFieldCode()
    {
        var supplier = Supplier.Register(
            "SUP-MSP85-TEST", "شركة اختبار", "MSP85 Test Co", null, "Rep", "msp85@example.com");

        var missing = supplier.GetMissingProfileFields();

        missing.Should().HaveCount(5, "currencyCode, address, categoryLink, primaryContactPhone, termsAccepted - legalInfo is already set by Register()");
        missing.Should().Contain("termsAccepted", "the one deliberate exception: no ProfileFieldCodes.TermsAccepted exists");

        foreach (var code in missing.Where(c => c != "termsAccepted"))
        {
            ProfileFieldCodes.IsKnown(code).Should().BeTrue(
                $"'{code}' is reported as a missing profile field, so a reviewer must be able to flag exactly that code - " +
                "if this fails, GetMissingProfileFields and ProfileFieldCodes have diverged");
        }
    }
}
