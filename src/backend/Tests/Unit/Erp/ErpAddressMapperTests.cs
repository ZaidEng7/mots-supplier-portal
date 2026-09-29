// Turning the ERP's billing address into a portal address, with its governorate.
//
// EVERY STREET LINE BELOW IS ONE THE REAL SERVER SENT, because the rules were written for that data and a hand-made
// line would test the rule's intention rather than what it does to Seven Gates' suppliers. Their city field says
// "Damascus" for all of them; the street is where the actual place was written.
//
// THE HOMSI STREET CASE IS THE ONE THAT MATTERS MOST. "شارع الحمصي" is a street in Jaramana, and a substring search
// finds "حمص" inside it and files the supplier under Homs. Whole-word matching is what stops that, and the test
// asserts the governorate the line actually names.
//
// THE CONTROL IS A LINE THAT NAMES NO GOVERNORATE, which must fall back to the city rather than come back empty -
// otherwise every test asserting a governorate from the line would pass against a mapper that ignored the city.

namespace MotsSupplierPortal.Tests.Unit.Erp;

using FluentAssertions;
using MotsSupplierPortal.Application.Integration;

public sealed class ErpAddressMapperTests
{
    private static ErpAddressMapping Map(string? line1, string? city = "Damascus", string? country = "Syria") =>
        ErpAddressMapper.Map(new ErpSupplierAddress(line1, null, city, country));

    [Theory]
    [InlineData("ريف دمشق - جرمانا شارع الحمصي عقار 645", "RDM")]
    [InlineData("ريف دمشق - الكسوة - دير علي", "RDM")]
    [InlineData("حلب العرقوب الصتاعية محضر6/4101 م ع تاسعة", "ALP")]
    [InlineData("دمشق-مزرعة شارع صالح العلي دخلة بنك الدم", "DIM")]
    [InlineData("Rif Dimashq, Jaramana", "RDM")]
    public void A_governorate_named_in_the_street_wins_over_the_city(string line, string expected)
    {
        Map(line).Address!.RegionCode.Should().Be(
            expected,
            "the street is where somebody wrote the place; the city field is where the ERP's default sat");
    }

    [Fact]
    public void Homsi_street_is_not_homs()
    {
        ErpAddressMapper.GovernorateIn("جرمانا شارع الحمصي").Should().BeNull(
            "'الحمصي' is a street name; matching fragments would file a Jaramana supplier under Homs");
    }

    [Theory]
    [InlineData("عقربا -المنطقة الصناعية-عقار1810", "Damascus")]
    [InlineData("مشروع دمر عند دوار ل 16", "DAMASCUS")]
    [InlineData("قدسيا - طلعة الحرس", "دمشق")]
    public void A_street_that_names_no_governorate_falls_back_to_the_city(string line, string city)
    {
        var mapped = Map(line, city).Address!;

        mapped.RegionCode.Should().Be("DIM");
        mapped.City.Should().Be(city, "the city is kept as the ERP wrote it");
        mapped.Country.Should().Be("SY");
    }

    [Fact]
    public void An_address_outside_syria_is_not_imported_and_says_so()
    {
        var mapping = Map("jordan", "JORDAN", "Jordan");

        mapping.Address.Should().BeNull("the portal's governorates are Syria's");
        mapping.Note.Should().Contain("Jordan");
    }

    [Theory]
    [InlineData(null, "Damascus", "street")]
    [InlineData("دمشق - المالكي", null, "city")]
    public void An_address_missing_a_street_or_a_city_is_not_imported(string? line, string? city, string missing)
    {
        var mapping = Map(line, city);

        mapping.Address.Should().BeNull();
        mapping.Note.Should().Contain(missing);
    }

    [Fact]
    public void An_address_whose_city_and_street_name_no_governorate_is_not_imported()
    {
        var mapping = Map("مشروع دمر", "Qudsaya");

        mapping.Address.Should().BeNull("inventing a governorate would put the supplier on the wrong map");
        mapping.Note.Should().Contain("names no Syrian governorate");
    }

    [Fact]
    public void No_address_at_all_is_stated()
    {
        var mapping = ErpAddressMapper.Map(null);

        mapping.Address.Should().BeNull();
        mapping.Note.Should().Contain("No address");
    }

    [Theory]
    [InlineData("حرستا - اوتوستراد حمص الدولي")]
    [InlineData("أوتوستراد درعا - جانب جامع")]
    [InlineData("طريق حمص")]
    public void A_road_named_after_a_city_does_not_decide_the_governorate(string line)
    {
        Map(line).Address!.RegionCode.Should().Be(
            "DIM", "the Homs highway starts in Damascus; naming a road after a city does not put the address there");
    }

    [Theory]
    [InlineData("Rif Damascus, Jaramana", "RDM")]
    [InlineData("Reef Damascus - Sahnaya", "RDM")]
    [InlineData("اللاذقيه - الزراعة", "LAT")]
    [InlineData("الحسكه - حي الناصرة", "HAS")]
    public void Spellings_people_actually_type_are_recognised(string line, string expected)
    {
        Map(line).Address!.RegionCode.Should().Be(expected, "an unknown spelling silently falls back to the city");
    }

    [Fact]
    public void A_street_longer_than_its_column_is_cut_and_the_note_says_so()
    {
        var mapping = Map("دمشق " + new string('س', 400));

        mapping.Address!.Line1.Should().HaveLength(ErpFieldLimits.AddressLine);
        mapping.Note.Should().Contain("first 300");
    }
}
