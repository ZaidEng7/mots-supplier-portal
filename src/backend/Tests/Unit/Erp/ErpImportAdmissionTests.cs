// Turning whatever the ERP sent into a supplier the portal can hold, with nobody left out.
//
// THE PLACEHOLDER IS THE PART WORTH TESTING HARD, because it becomes a login. Three things about it matter and
// each has a test: it can never deliver mail, it is the same on every run, and two different suppliers can never
// be given the same one - since two suppliers cannot share an account, a collision would silently refuse the
// second of them on a run meant to leave nobody out.
//
// THE COLLISION CASE IS NOT HYPOTHETICAL. Turning "A & B Trading" and "A-B Trading" into something an address can
// hold makes them the same text; the short hash of the original identifier is what keeps them apart, and the test
// uses exactly that pair.
//
// THE CONTROL IS A SUPPLIER WITH EVERYTHING: no placeholder, no notes about gaps. If it came back with a
// placeholder, every test below that looks for one would be passing for the wrong reason.

namespace MotsSupplierPortal.Tests.Unit.Erp;

using FluentAssertions;
using MotsSupplierPortal.Application.Integration;

public sealed class ErpImportAdmissionTests
{
    private static ErpSupplier Supplier(
        string externalId = "Damascus Supplies Co",
        string? name = "Damascus Supplies Co",
        string? email = "contact@example.sy",
        string? currency = "SYP",
        bool disabled = false) =>
        new(externalId, name, "Local", "Company", "TAX-1", "Syria", email, "+963", disabled, currency, null, null,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    [Fact]
    public void A_supplier_with_everything_is_admitted_as_it_is()
    {
        var admitted = ErpImportAdmission.Admit(Supplier());

        admitted.Email.Should().Be("contact@example.sy");
        admitted.EmailIsPlaceholder.Should().BeFalse();
        admitted.Currency.Should().Be("SYP");
        admitted.Suspended.Should().BeFalse();
        admitted.Notes.Should().BeEmpty();
    }

    [Fact]
    public void A_supplier_with_no_email_is_given_a_placeholder_that_cannot_deliver()
    {
        var admitted = ErpImportAdmission.Admit(Supplier(externalId: "Tartous Beverages (seed)", email: null));

        admitted.EmailIsPlaceholder.Should().BeTrue();
        admitted.Email.Should().EndWith(
            "@erp-import.invalid",
            ".invalid is reserved so that it can never deliver - an account on it can receive nothing");
        admitted.Email.Should().StartWith("tartous-beverages-seed-");
        admitted.Notes.Should().ContainMatch("*placeholder*replace it*");
        ErpImportAdmission.IsPlaceholder(admitted.Email).Should().BeTrue();
    }

    [Fact]
    public void The_same_supplier_gets_the_same_placeholder_on_every_run()
    {
        ErpImportAdmission.PlaceholderEmail("Tartous Beverages (seed)").Should().Be(
            ErpImportAdmission.PlaceholderEmail("Tartous Beverages (seed)"),
            "a placeholder that changed each run would read as a real update in every report");
    }

    [Fact]
    public void Two_suppliers_whose_names_read_the_same_as_an_address_still_get_different_placeholders()
    {
        ErpImportAdmission.PlaceholderEmail("A & B Trading").Should().NotBe(
            ErpImportAdmission.PlaceholderEmail("A-B Trading"),
            "both become a-b-trading as text, and two suppliers cannot share one account");
    }

    [Fact]
    public void A_real_address_is_not_mistaken_for_a_placeholder()
    {
        ErpImportAdmission.IsPlaceholder("sales@homs-linen.sgtest.example").Should().BeFalse();
        ErpImportAdmission.IsPlaceholder(null).Should().BeFalse();
    }

    [Fact]
    public void An_unknown_currency_is_left_empty_rather_than_refused_or_guessed()
    {
        var admitted = ErpImportAdmission.Admit(Supplier(currency: "EUR"));

        admitted.Currency.Should().BeNull("defaulting to SYP would silently reprice the supplier");
        admitted.Notes.Should().ContainMatch("*'EUR'*left empty*");
    }

    [Fact]
    public void A_supplier_with_no_name_is_named_after_its_identifier()
    {
        ErpImportAdmission.Admit(Supplier(externalId: "SUP-2026-00017", name: "  ")).Name.Should().Be("SUP-2026-00017");
    }

    [Fact]
    public void A_supplier_disabled_in_the_erp_arrives_suspended()
    {
        var admitted = ErpImportAdmission.Admit(Supplier(disabled: true));

        admitted.Suspended.Should().BeTrue(
            "active would let a company Seven Gates has stopped using be invited to tenders");
        admitted.Notes.Should().ContainMatch("*suspended*cannot be invited*");
    }
}
