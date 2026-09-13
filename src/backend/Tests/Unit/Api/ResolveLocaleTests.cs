// Parsing the browser's language header, which is free text a browser controls rather than a clean set of values.
//
// This proves the parser handles the shapes real browsers actually send, including quality values, region subtags
// and multiple entries, and that it defaults safely rather than throwing or guessing at the wrong language.

namespace MotsSupplierPortal.Tests.Unit.Api;

using FluentAssertions;
using MotsSupplierPortal.Api.Endpoints;

public sealed class ResolveLocaleTests
{
    [Theory]
    [InlineData(null, "ar")]
    [InlineData("", "ar")]
    [InlineData("   ", "ar")]
    [InlineData("ar", "ar")]
    [InlineData("en", "en")]
    [InlineData("en-US", "en")]
    [InlineData("en-US,en;q=0.9", "en")]
    [InlineData("ar-SY,ar;q=0.9,en;q=0.8", "ar")]
    [InlineData("fr", "ar")] // unsupported language falls back to Arabic, not English
    [InlineData("EN", "en")] // case-insensitive
    [InlineData("*", "ar")]
    public void Resolves_the_primary_subtag_of_the_first_entry_and_defaults_to_Arabic(string? header, string expected) =>
        RegistrationEndpoints.ResolveLocale(header).Should().Be(expected);
}
