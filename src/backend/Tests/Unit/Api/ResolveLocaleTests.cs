using FluentAssertions;
using MotsSupplierPortal.Api.Endpoints;

namespace MotsSupplierPortal.Tests.Unit.Api;

/// <summary>MSP-69: the Accept-Language header is free text a browser controls, not a clean enum -
/// this proves the parser handles the shapes real browsers actually send (q-values, region subtags,
/// multiple entries) and defaults safely rather than throwing or guessing at the wrong locale.</summary>
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
