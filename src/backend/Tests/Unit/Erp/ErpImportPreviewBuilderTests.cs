// What the import would do with each supplier the ERP sent.
//
// THE DENOMINATOR IS ASSERTED. Every test that counts outcomes also checks the three counts sum to the number of
// suppliers given, because a rule that quietly dropped a row would otherwise show up as a smaller number that
// still looks plausible - and an import whose preview undercounts is worse than no preview.
//
// NOBODY IS REFUSED. Every supplier in the ERP is meant to appear in the portal, so the tests that used to expect
// a refusal - no email, no name, an unknown currency - now expect a create with the gap filled and noted. How
// each gap is filled is tested directly in ErpImportAdmissionTests; these check the forecast carries it.
//
// THE NULL-TAX-NUMBER CASE IS NOT PEDANTRY. Most suppliers have no tax number, and a duplicate check that
// grouped them under an empty key would flag a possible duplicate on every single row - which is how a warning
// becomes something people scroll past.
//
// AN UNKNOWN CURRENCY IS LEFT EMPTY, NOT DEFAULTED. Defaulting to SYP would silently reprice a relationship and
// the ministry's feed would report that price as fact; empty says "not known", which is true.

namespace MotsSupplierPortal.Tests.Unit.Erp;

using FluentAssertions;
using MotsSupplierPortal.Application.Integration;

public sealed class ErpImportPreviewBuilderTests
{
    private static readonly Dictionary<string, ErpImportCandidateMatch> NoMatches = [];
    private static readonly Dictionary<string, string> NoUnlinked = [];

    private static ErpSupplier Supplier(
        string externalId = "Damascus Supplies Co",
        string? name = "Damascus Supplies Co",
        string? email = "contact@example.sy",
        string? currency = "SYP",
        string? taxId = "TAX-100",
        string? phone = "+963 11 000 0000",
        string? group = "Local",
        string? primaryAddress = "Damascus Supplies Co-Billing",
        bool disabled = false) =>
        new(
            externalId,
            name,
            group,
            "Company",
            taxId,
            "Syria",
            email,
            phone,
            disabled,
            currency,
            primaryAddress,
            "Damascus Supplies Co-Contact",
            DateTimeOffset.Parse("2026-09-16T12:33:48+03:00"),
            DateTimeOffset.Parse("2026-09-16T12:33:48+03:00"));

    private static void CountsAddUp(ErpImportPreviewReport report)
    {
        (report.WouldCreate + report.WouldUpdate + report.Refused).Should().Be(
            report.ErpSupplierCount,
            "a rule that dropped a row would otherwise show as a smaller number that still looks plausible");
        report.Rows.Should().HaveCount(report.ErpSupplierCount);
    }

    [Fact]
    public void A_supplier_with_everything_would_be_created_with_nothing_refused()
    {
        var report = ErpImportPreviewBuilder.Build([Supplier()], NoMatches, NoUnlinked);

        CountsAddUp(report);
        report.WouldCreate.Should().Be(1);
        report.Refused.Should().Be(0);
        report.Rows[0].Action.Should().Be(ErpImportAction.Create);
        report.Rows[0].MatchedReferenceCode.Should().BeNull();
    }

    [Fact]
    public void A_supplier_with_no_email_would_be_created_with_a_placeholder_rather_than_refused()
    {
        var report = ErpImportPreviewBuilder.Build([Supplier(email: null)], NoMatches, NoUnlinked);

        CountsAddUp(report);
        report.WouldCreate.Should().Be(1, "every supplier in the ERP is meant to appear in the portal");
        report.Refused.Should().Be(0);
        report.Rows[0].Notes.Should().ContainMatch("*placeholder*@erp-import.invalid*");
    }

    [Fact]
    public void A_supplier_with_no_name_would_be_created_under_its_identifier()
    {
        var report = ErpImportPreviewBuilder.Build([Supplier(name: "  ")], NoMatches, NoUnlinked);

        report.Rows[0].Action.Should().Be(ErpImportAction.Create);
        report.Rows[0].Name.Should().Be("Damascus Supplies Co");
    }

    [Fact]
    public void A_currency_the_portal_does_not_know_is_left_empty_rather_than_defaulted_to_syp()
    {
        var report = ErpImportPreviewBuilder.Build([Supplier(currency: "EUR")], NoMatches, NoUnlinked);

        report.Rows[0].Action.Should().Be(ErpImportAction.Create);
        report.Rows[0].Notes.Should().ContainMatch("*'EUR'*left empty*");
    }

    [Fact]
    public void A_supplier_the_portal_already_carries_would_be_updated()
    {
        var matches = new Dictionary<string, ErpImportCandidateMatch>
        {
            ["Damascus Supplies Co"] = new("SUP-2026-000004", "TAX-100"),
        };

        var report = ErpImportPreviewBuilder.Build([Supplier()], matches, NoUnlinked);

        CountsAddUp(report);
        report.WouldUpdate.Should().Be(1);
        report.Rows[0].Action.Should().Be(ErpImportAction.Update);
        report.Rows[0].MatchedReferenceCode.Should().Be("SUP-2026-000004");
    }

    [Fact]
    public void A_tax_number_already_on_a_self_registered_supplier_is_reported_rather_than_merged()
    {
        var unlinked = new Dictionary<string, string> { ["TAX-100"] = "SUP-2026-000009" };

        var report = ErpImportPreviewBuilder.Build([Supplier()], NoMatches, unlinked);

        report.Rows[0].Action.Should().Be(
            ErpImportAction.Create,
            "merging on a field that is sometimes blank and sometimes shared between a company and its "
            + "subsidiary is not a decision this should make");
        report.Rows[0].Notes.Should().ContainMatch("*TAX-100*SUP-2026-000009*Needs a person*");
    }

    [Fact]
    public void A_supplier_with_no_tax_number_never_collides_with_one()
    {
        // The empty-string key is the trap, and it has to be IN the dictionary for this test to mean anything.
        // A lookup written as "TaxId ?? string.Empty" misses a dictionary that holds only real tax numbers, so a
        // version of this test without that entry passes whether the guard exists or not - which is how it was
        // first written here, and it caught nothing.
        var unlinked = new Dictionary<string, string>
        {
            ["TAX-100"] = "SUP-2026-000009",
            [string.Empty] = "SUP-2026-000010",
        };

        var report = ErpImportPreviewBuilder.Build([Supplier(taxId: null)], NoMatches, unlinked);

        report.Rows[0].Notes.Should().NotContainMatch("*Needs a person*",
            "most suppliers have no tax number, and flagging every one of them buries the real collisions");
    }

    [Fact]
    public void The_gaps_the_erp_cannot_fill_are_stated_on_every_row()
    {
        var report = ErpImportPreviewBuilder.Build(
            [Supplier(group: "Raw Material", primaryAddress: null, phone: null)], NoMatches, NoUnlinked);

        report.Rows[0].Notes.Should().ContainMatch("*'Raw Material' has no portal category*");
        report.Rows[0].Notes.Should().ContainMatch("*No address*governorate*");
        report.Rows[0].Notes.Should().ContainMatch("*Arabic name starts as the English one*");
        report.Rows[0].Notes.Should().ContainMatch("*No phone*");
    }

    [Fact]
    public void A_supplier_disabled_in_the_erp_is_noted_as_arriving_suspended()
    {
        var report = ErpImportPreviewBuilder.Build([Supplier(disabled: true)], NoMatches, NoUnlinked);

        report.Rows[0].Notes.Should().ContainMatch("*Disabled in the ERP*suspended*");
    }

    [Fact]
    public void A_mixed_run_leaves_nobody_out()
    {
        ErpSupplier[] suppliers =
        [
            Supplier("A", "A"),
            Supplier("B", "B", email: null),
            Supplier("C", "C", currency: "EUR"),
            Supplier("D", "D"),
        ];

        var matches = new Dictionary<string, ErpImportCandidateMatch>
        {
            ["D"] = new("SUP-2026-000001", null),
        };

        var report = ErpImportPreviewBuilder.Build(suppliers, matches, NoUnlinked);

        CountsAddUp(report);
        report.ErpSupplierCount.Should().Be(4);
        report.WouldCreate.Should().Be(3, "the two with gaps are filled in, not left out");
        report.WouldUpdate.Should().Be(1);
        report.Refused.Should().Be(0);
    }
}
