using FluentAssertions;
using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Tests.Unit.Domain;

/// <summary>
/// Task #18/MSP-85: <c>Supplier.GetMissingProfileFields()</c> feeds SupplierDto.MissingProfileFields,
/// which the frontend reads by string equality against the SAME vocabulary <c>ProfileFieldCodes</c>
/// defines for what a reviewer may flag (OnboardingPage.tsx's own comment names this exact
/// dependency: "keep in sync if the backend list changes"). Before this fix, that sync existed only
/// because two independent string literals happened to agree - nothing would have caught a
/// ProfileFieldCodes rename silently orphaning a missing-field indicator.
///
/// This test would have failed against the pre-fix code the moment a maintainer renumbered/renamed
/// a ProfileFieldCodes constant without also updating the matching literal in
/// GetMissingProfileFields - which is exactly the failure mode the ticket describes ("someone
/// renames a DTO property without realizing it's also a flagging code"). Now it cannot happen: the
/// values are the same constant, so a rename is a compile error in Supplier.cs itself, not a silent
/// runtime divergence this test would be the only thing to catch.
/// </summary>
public sealed class GetMissingProfileFieldsVocabularyTests
{
    [Fact]
    public void Every_missing_field_code_except_termsAccepted_is_a_known_ProfileFieldCode()
    {
        // Register() already sets LegalInfo (from the display names) - so a freshly-registered
        // supplier is missing everything EXCEPT legalInfo: currencyCode, address, categoryLink,
        // primaryContactPhone (no phone given), and termsAccepted. Five items - the denominator
        // this test actually exercises, stated explicitly rather than assumed.
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
