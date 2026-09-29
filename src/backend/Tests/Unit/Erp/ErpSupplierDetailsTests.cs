// What the supplier keeps of the ERP's details, on the first import and on every run after it.
//
// A NULL NEVER BLANKS A VALUE. The ERP is master for the fields it sends, and "it sent nothing" means "it does not
// know", not "delete what you have". Each field is asserted both ways: a value from the ERP replaces the old one,
// and a missing one leaves it.
//
// THE REGISTRATION TYPE SURVIVES THE SUPPLIER'S OWN EDIT of anything but the number. Nobody types it in the portal -
// the legal-details form has no field for it - so a supplier correcting their tax number must not lose the type the
// ERP recorded; one who changes the number drops it, because it described the old number.
//
// THE REPRESENTATIVE IS RENAMED ONLY FROM THE COMPANY'S NAME. The import names a new supplier's representative after
// the company when the ERP has no person; a later run fills that in. A person's name already there is the portal's -
// the supplier may have made somebody else primary - and is never overwritten.

namespace MotsSupplierPortal.Tests.Unit.Erp;

using FluentAssertions;
using MotsSupplierPortal.Domain.Suppliers;

public sealed class ErpSupplierDetailsTests
{
    private static Supplier Imported(ErpSupplierDetails? details = null) =>
        Supplier.ImportFromErp(
            "SUP-2026-000001", "SUP-2026-00001", "AL-Zaeim for advertising services", "01010484153",
            SupplierLegalType.Company, "SYP", "AL-Zaeim for advertising services", "placeholder@erp-import.invalid",
            "0944444931",
            details: details ?? new ErpSupplierDetails(
                "الزعيم للخدمات الإعلانية", "73260", "Commercial", "مستلزمات مكتبية - SYP", null));

    [Fact]
    public void The_first_import_carries_every_detail_the_erp_has()
    {
        var supplier = Imported();

        supplier.DisplayNameAr.Should().Be("الزعيم للخدمات الإعلانية");
        supplier.LegalInfo!.LegalNameAr.Should().Be("الزعيم للخدمات الإعلانية");
        supplier.LegalInfo.RegistrationNumber.Should().Be("73260");
        supplier.LegalInfo.RegistrationType.Should().Be("Commercial");
        supplier.SupplierGroup.Should().Be("مستلزمات مكتبية - SYP");
        supplier.Description.Should().BeNull("the ERP has none, and nothing is invented for it");
    }

    [Fact]
    public void With_no_arabic_name_the_required_field_starts_as_the_english_one()
    {
        var supplier = Imported(new ErpSupplierDetails(null, null, null, null, null));

        supplier.DisplayNameAr.Should().Be("AL-Zaeim for advertising services");
        supplier.LegalInfo!.RegistrationNumber.Should().BeNull();
        supplier.LegalInfo.RegistrationType.Should().BeNull();
        supplier.SupplierGroup.Should().BeNull();
    }

    [Fact]
    public void A_later_run_replaces_what_the_erp_sends_and_keeps_what_it_does_not()
    {
        var supplier = Imported();

        supplier.ApplyErpSnapshot(
            "AL-Zaeim for advertising services", null, SupplierLegalType.Company, null, null, null,
            details: new ErpSupplierDetails("الزعيم للإعلان", null, null, null, "Printing and signage"),
            representativeName: null);

        supplier.DisplayNameAr.Should().Be("الزعيم للإعلان");
        supplier.LegalInfo!.LegalNameAr.Should().Be("الزعيم للإعلان");
        supplier.Description.Should().Be("Printing and signage");
        supplier.LegalInfo.RegistrationNumber.Should().Be("73260", "a missing number is not a deleted one");
        supplier.LegalInfo.RegistrationType.Should().Be("Commercial");
        supplier.SupplierGroup.Should().Be("مستلزمات مكتبية - SYP");
        supplier.Representatives[0].FullName.Should().Be("AL-Zaeim for advertising services");

        supplier.ApplyErpSnapshot(
            "AL-Zaeim for advertising services", null, SupplierLegalType.Company, null, null, null,
            details: new ErpSupplierDetails(null, "73261", "Industrial", "مواد غذائية - SYP", null));

        supplier.LegalInfo!.RegistrationNumber.Should().Be("73261");
        supplier.LegalInfo.RegistrationType.Should().Be("Industrial");
        supplier.SupplierGroup.Should().Be("مواد غذائية - SYP");
        supplier.DisplayNameAr.Should().Be("الزعيم للإعلان", "no Arabic name arrived this time");
        supplier.Description.Should().Be("Printing and signage");
    }

    [Fact]
    public void A_person_named_by_a_later_run_replaces_the_company_name_the_import_stood_in_with()
    {
        var supplier = Imported();

        supplier.ApplyErpSnapshot(
            "AL-Zaeim for advertising services", null, SupplierLegalType.Company, null, null, null,
            representativeName: "جهاد سنديان");

        supplier.Representatives[0].FullName.Should().Be("جهاد سنديان");
    }

    [Fact]
    public void A_person_already_named_on_the_representative_is_not_overwritten()
    {
        var supplier = Imported();
        supplier.Representatives[0].FullName = "Sara Khalil";

        supplier.ApplyErpSnapshot(
            "AL-Zaeim for advertising services", null, SupplierLegalType.Company, null, null, null,
            representativeName: "Ahmad Omar");

        supplier.Representatives[0].FullName.Should().Be(
            "Sara Khalil",
            "the supplier may have made somebody else primary; renaming them would put one person's name on another's "
            + "account");
    }

    [Fact]
    public void Changing_the_registration_number_drops_the_type_that_described_the_old_one()
    {
        var supplier = Imported();

        supplier.UpdateLegalInfo(
            "الزعيم للخدمات الإعلانية", "AL-Zaeim for advertising services", "IND-5501", "01010484153",
            SupplierLegalType.Company, null, isComplianceCritical: true);

        supplier.LegalInfo!.RegistrationType.Should().BeNull();
    }

    [Fact]
    public void The_suppliers_own_legal_edit_keeps_the_registration_type_the_erp_recorded()
    {
        var supplier = Imported();

        supplier.UpdateLegalInfo(
            "الزعيم للخدمات الإعلانية", "AL-Zaeim for advertising services", "73260", "01010484153-A",
            SupplierLegalType.Company, null, isComplianceCritical: true);

        supplier.LegalInfo!.RegistrationType.Should().Be(
            "Commercial", "the form has no field for it, so an edit that dropped it would lose it for good");
        supplier.LegalInfo.TaxId.Should().Be("01010484153-A");
    }
}
