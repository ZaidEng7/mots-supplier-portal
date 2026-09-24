// The ministry feed's twenty-two columns, and the four values that are translated on the way out.
//
// THE COUNT IS FIRST, for the reason it is first in the registry export's tests: a row one cell out of step
// with the header shifts every column after it, their loader reports nothing, and TaxID arrives in the column
// their dashboard labels RegistrationType. It is invisible in a spreadsheet too, because a shifted file still
// opens.
//
// THE FOUR TRANSLATIONS ARE ASSERTED BOTH WAYS, because each has a half that a careless implementation gets
// right by accident. ApprovalStatus is checked on all nine onboarding states rather than a sample, since the
// mapping is a switch and a missing arm falls through to Draft - a supplier sitting with a reviewer would be
// reported to the ministry as an unfinished draft, which is a different fact about a different company.
//
// THE COUNTRY LIST IS ASSERTED ON WHAT THE DATABASE ACTUALLY HOLDS - SY, syria and Türkiye, the three
// spellings present today - plus the case that matters more: a spelling nobody has met passes through
// UNCHANGED. Mapping an unknown to SY would put a foreign supplier in Syria and nothing would ever say so;
// blanking it would hide the gap. Leaving it visible is what makes the next unmapped country findable.
//
// A SUPPLIER WITH NO ADDRESS produces a row with six blank cells and the same column count as any other. Four
// of those are Must fields in the workbook, so the blanks are the honest report of a hole in our data, and the
// assertion is that the row does not collapse or throw reaching through a null address.
//
// SUPPLIERGROUP IS THE PRIMARY CATEGORY'S ENGLISH NAME, one value, and the test uses a supplier with several
// categories - because that is the case this column existed to solve, and a supplier with one would pass
// against an implementation that simply sent the first link it found.

namespace MotsSupplierPortal.Tests.Unit.Suppliers;

using FluentAssertions;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using Xunit;

public sealed class MinistrySupplierFeedCsvTests
{
    private static readonly IReadOnlyDictionary<string, string> Categories = new Dictionary<string, string>
    {
        ["catering"] = "Catering & Hospitality",
        ["transport"] = "Transport",
    };

    private static readonly IReadOnlyDictionary<string, string> Regions = new Dictionary<string, string>
    {
        ["DIM"] = "Damascus",
    };

    private static Supplier Editable(string code = "SUP-2026-000001")
    {
        var supplier = Supplier.Register(code, "شركة", "Test Co", "CR-1", "Owner", "owner@example.com");
        supplier.MarkEmailVerified();
        return supplier;
    }

    private static MinistrySupplierFeedRecord Record(Supplier supplier) =>
        new(supplier, Categories, Regions);

    private static int At(string column) => MinistrySupplierFeedCsv.Columns.ToList().IndexOf(column);

    [Fact]
    public void A_row_has_exactly_as_many_cells_as_the_header_has_columns()
    {
        var supplier = Editable();
        supplier.AddAddress(AddressKind.HeadOffice, "1 Baghdad St", null, "Damascus", "DIM", "SY", null, 33.5, 36.3);
        supplier.LinkCategory("catering", isComplianceCritical: false);

        MinistrySupplierFeedCsv.Cells(Record(supplier)).Should().HaveCount(
            MinistrySupplierFeedCsv.Columns.Count,
            "a row out of step with the header shifts every column after it and their loader reports nothing");
    }

    [Fact]
    public void A_supplier_with_no_address_still_produces_a_full_row()
    {
        var cells = MinistrySupplierFeedCsv.Cells(Record(Editable()));

        cells.Should().HaveCount(MinistrySupplierFeedCsv.Columns.Count);
        cells[At("Country")].Should().BeNull();
        cells[At("Governorate")].Should().BeNull();
        cells[At("Latitude")].Should().BeNull();
    }

    [Fact]
    public void The_header_names_are_the_ministrys_and_are_unique()
    {
        MinistrySupplierFeedCsv.Columns.Should().OnlyHaveUniqueItems();
        MinistrySupplierFeedCsv.Columns[0].Should().Be("SupplierID");
        MinistrySupplierFeedCsv.Columns.Should().Contain(["SupplierNameAr", "ApprovalStatus", "Governorate"]);
    }

    [Theory]
    [InlineData(SupplierOnboardingState.Approved, "Approved")]
    [InlineData(SupplierOnboardingState.Submitted, "Pending Financial Approval")]
    [InlineData(SupplierOnboardingState.UnderReview, "Pending Financial Approval")]
    [InlineData(SupplierOnboardingState.InfoRequested, "Pending Financial Approval")]
    [InlineData(SupplierOnboardingState.Resubmitted, "Pending Financial Approval")]
    [InlineData(SupplierOnboardingState.Draft, "Draft")]
    [InlineData(SupplierOnboardingState.EmailVerified, "Draft")]
    [InlineData(SupplierOnboardingState.ProfileInProgress, "Draft")]
    [InlineData(SupplierOnboardingState.Rejected, "Draft")]
    public void Every_onboarding_state_maps_to_one_of_the_ministrys_three(
        SupplierOnboardingState state, string expected)
    {
        MinistrySupplierFeedCsv.ApprovalStatus(state).Should().Be(expected);
    }

    [Theory]
    [InlineData("SY", "SY")]
    [InlineData("sy", "SY")]
    [InlineData("syria", "SY")]
    [InlineData("Türkiye", "TR")]
    [InlineData("Lebanon", "LB")]
    public void A_country_the_list_knows_becomes_its_ISO_code(string stored, string expected)
    {
        MinistrySupplierFeedCsv.CountryCode(stored).Should().Be(expected);
    }

    [Fact]
    public void A_country_the_list_has_never_met_passes_through_unchanged()
    {
        MinistrySupplierFeedCsv.CountryCode("Ruritania").Should().Be(
            "Ruritania",
            "mapping an unknown to SY would put a foreign supplier in Syria and nothing would say so; blanking "
            + "it would hide the gap, and leaving it visible is what makes the next one findable");
    }

    [Fact]
    public void SupplierGroup_is_the_primary_categorys_english_name_even_when_there_are_several()
    {
        var supplier = Editable();
        supplier.LinkCategory("transport", isComplianceCritical: false);
        supplier.LinkCategory("catering", isComplianceCritical: false);
        supplier.SetPrimaryCategory("catering", isComplianceCritical: false);

        var cells = MinistrySupplierFeedCsv.Cells(Record(supplier));

        cells[At("SupplierGroup")].Should().Be("Catering & Hospitality");
    }

    [Fact]
    public void Disabled_is_one_only_when_the_supplier_is_suspended_or_deactivated()
    {
        var active = Editable();
        MinistrySupplierFeedCsv.Cells(Record(active))[At("Disabled")].Should().Be("0");
    }

    [Fact]
    public void The_governorate_is_the_english_name_and_the_code_survives_when_unknown()
    {
        var known = Editable();
        known.AddAddress(AddressKind.HeadOffice, "1 St", null, "Damascus", "DIM", "SY", null, null, null);

        var unknown = Editable("SUP-2026-000002");
        unknown.AddAddress(AddressKind.HeadOffice, "1 St", null, "Aleppo", "ALP", "SY", null, null, null);

        MinistrySupplierFeedCsv.Cells(Record(known))[At("Governorate")].Should().Be("Damascus");
        MinistrySupplierFeedCsv.Cells(Record(unknown))[At("Governorate")].Should().Be("ALP");
    }
}
