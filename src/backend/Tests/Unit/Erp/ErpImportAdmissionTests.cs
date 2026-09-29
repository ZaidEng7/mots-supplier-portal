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
//
// THE ADDRESS'S FATE IS TESTED APART FROM ITS MAPPING, because it depends on the supplier the portal already has. The
// first version wrote the ERP's address note onto every row, so an update that kept the portal's own address still
// said "Address imported" - and tried to add one to a supplier under review, which the domain refuses.
//
// THE FIELDS THE REAL SERVER ADDED ARE TESTED THE SAME WAY: an Arabic name, a contact person and an approval state are
// used when the ERP has them, and when it does not the field stays empty or falls back, with a note saying which.

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
        bool disabled = false,
        string? arabicName = "مؤسسة دمشق للتوريدات",
        string? person = "سامر الحلبي",
        string? workflowState = "Approved") =>
        new(externalId, name, "Local", "Company", "TAX-1", "Syria", email, "+963", disabled, currency, null, null,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            ArabicName: arabicName,
            WorkflowState: workflowState,
            ContactPersonName: person,
            Address: new ErpSupplierAddress("دمشق - المالكي", null, "Damascus", "Syria"));

    [Fact]
    public void A_supplier_with_everything_is_admitted_as_it_is()
    {
        var admitted = ErpImportAdmission.Admit(Supplier());

        admitted.Email.Should().Be("contact@example.sy");
        admitted.EmailIsPlaceholder.Should().BeFalse();
        admitted.Currency.Should().Be("SYP");
        admitted.Suspended.Should().BeFalse();
        admitted.ArabicName.Should().Be("مؤسسة دمشق للتوريدات");
        admitted.RepresentativeName.Should().Be("سامر الحلبي");
        admitted.Address!.Address!.RegionCode.Should().Be("DIM");
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
        admitted.ArrivalNote.Should().Match("*suspended*cannot be invited*");
        admitted.TurnedAway.Should().Be("Disabled in the ERP");
    }

    [Theory]
    [InlineData("Pending Chief Accountant Approval")]
    [InlineData("Draft")]
    public void A_supplier_the_erp_has_not_approved_arrives_suspended_and_names_its_state(string state)
    {
        var admitted = ErpImportAdmission.Admit(Supplier(workflowState: state));

        admitted.Suspended.Should().BeTrue("a record still waiting for approval there is not one to invite here");
        admitted.ArrivalNote.Should().Match($"*'{state}'*suspended*");
        admitted.TurnedAway.Should().Be($"Not approved in the ERP ('{state}')");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("approved")]
    public void A_supplier_with_no_workflow_or_an_approved_one_arrives_active(string? state)
    {
        ErpImportAdmission.Admit(Supplier(workflowState: state)).Suspended.Should().BeFalse(
            "a server with no approval workflow still has suppliers, and they are not all pending");
    }

    [Fact]
    public void With_no_arabic_name_the_field_is_empty_here_and_the_note_says_what_the_portal_falls_back_to()
    {
        var admitted = ErpImportAdmission.Admit(Supplier(arabicName: "  "));

        admitted.ArabicName.Should().BeNull();
        admitted.Notes.Should().ContainMatch("*No Arabic name*keeps its own*starts with the English name*");
    }

    [Fact]
    public void With_no_contact_person_the_representative_is_named_after_the_company_and_the_note_says_so()
    {
        var admitted = ErpImportAdmission.Admit(Supplier(person: null));

        admitted.ContactPerson.Should().BeNull();
        admitted.RepresentativeName.Should().Be("Damascus Supplies Co");
        admitted.Notes.Should().ContainMatch("*No contact person*named after the company*");
    }

    [Fact]
    public void A_new_supplier_takes_the_erps_address()
    {
        var outcome = ErpImportAdmission.AddressOutcome(
            ErpImportAdmission.Admit(Supplier()), isNew: true, addressesInPortal: 0, blockedByState: null);

        outcome.Write.Should().BeTrue();
        outcome.Note.Should().StartWith("Address imported");
    }

    [Fact]
    public void A_supplier_that_already_has_an_address_keeps_it_and_the_note_says_so()
    {
        var outcome = ErpImportAdmission.AddressOutcome(
            ErpImportAdmission.Admit(Supplier()), isNew: false, addressesInPortal: 1, blockedByState: null);

        outcome.Write.Should().BeFalse();
        outcome.Note.Should().Contain("already has an address").And.NotContain("imported");
    }

    [Fact]
    public void A_supplier_whose_details_cannot_be_edited_gets_no_address_and_is_told_why()
    {
        var outcome = ErpImportAdmission.AddressOutcome(
            ErpImportAdmission.Admit(Supplier()), isNew: false, addressesInPortal: 0, blockedByState: "in state 'UnderReview'");

        outcome.Write.Should().BeFalse(
            "the domain refuses a contact edit under review, and the first version let that refusal fail the whole update");
        outcome.Note.Should().Contain("UnderReview");
    }

    [Fact]
    public void A_description_longer_than_its_column_is_cut_and_the_note_says_so()
    {
        var admitted = ErpImportAdmission.Admit(Supplier() with { Description = new string('x', 2500) });

        admitted.Description.Should().HaveLength(ErpFieldLimits.Description);
        admitted.Notes.Should().ContainMatch("*description is 2500 characters*first 2000*");
    }

    [Fact]
    public void A_group_or_registration_type_longer_than_its_column_is_left_empty_not_cut()
    {
        var admitted = ErpImportAdmission.Admit(
            Supplier() with { SupplierGroup = new string('g', 140), RegistrationType = new string('t', 60) });

        admitted.SupplierGroup.Should().BeNull("a group name cut short is a different group that looks right");
        admitted.RegistrationType.Should().BeNull();
        admitted.Notes.Should().ContainMatch("*supplier group*left empty*");
        admitted.Notes.Should().ContainMatch("*registration type*left empty*");
    }
}
