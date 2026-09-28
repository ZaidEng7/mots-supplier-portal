// Finding a supplier's email when the supplier record does not carry it.
//
// THE CASE THAT MATTERS IS THE SECOND TEST, and it is not hypothetical: on the Seven Gates test instance the
// administrator linked a contact to a supplier and ticked "Is Primary Contact" on the contact, which is the
// obvious thing to do and does NOT populate the supplier's own email. Every supplier there read as having no
// email while the addresses sat one table over. An import that trusted the supplier record alone would have
// reported that none of them could have portal accounts.
//
// THE FIRST TEST IS THE CONTROL IN BOTH DIRECTIONS: a supplier whose own email is set keeps it, and the contact
// does not overwrite it. Their staff maintain that field deliberately; a contact that disagrees is a stale row,
// not a correction.
//
// THE TIE-BREAK IS ASSERTED because two contacts with no primary among them would otherwise resolve to whichever
// the database returned first, which is not a promise. This product has already shipped that bug once, in the
// ministry feed, where one supplier's address read "Line 2" in the CSV and "Line 0" in the JSON seconds apart.
//
// EMAIL AND PHONE RESOLVE SEPARATELY, so a supplier carrying one and not the other picks up only what it lacks.
// Taking both from whichever contact supplied the email would drop a phone number that was sitting right there.

namespace MotsSupplierPortal.Tests.Unit.Erp;

using FluentAssertions;
using MotsSupplierPortal.Application.Integration;
using MotsSupplierPortal.Infrastructure.Integration.Erp;

public sealed class ErpContactMergeTests
{
    private static ErpSupplier Supplier(string id, string? email = null, string? phone = null) =>
        new(id, id, "Local", "Company", null, "Syria", email, phone, false, "SYP", null, null,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private static ErpSupplierContact Contact(
        string contactName, string supplier, string? email = null, string? phone = null, bool primary = false) =>
        new(contactName, supplier, email, phone, primary);

    [Fact]
    public void A_supplier_that_carries_its_own_email_keeps_it()
    {
        var merged = ErpContactMerge.Fill(
            [Supplier("A", email: "on-the-supplier@example.com", phone: "+963 11 000")],
            [Contact("c1", "A", email: "on-the-contact@example.com", phone: "+963 99 999", primary: true)]);

        merged[0].Email.Should().Be(
            "on-the-supplier@example.com",
            "their staff maintain that field deliberately; a contact that disagrees is a stale row");
        merged[0].Phone.Should().Be("+963 11 000");
    }

    [Fact]
    public void A_supplier_with_no_email_takes_the_one_on_its_linked_contact()
    {
        var merged = ErpContactMerge.Fill(
            [Supplier("Homs Linen Mills (seed)")],
            [Contact("sales", "Homs Linen Mills (seed)", email: "sales@homs-linen.example", phone: "+963 31 555")]);

        merged[0].Email.Should().Be(
            "sales@homs-linen.example",
            "this is the real shape of the test instance: linked contact, supplier's own field empty");
        merged[0].Phone.Should().Be("+963 31 555");
    }

    [Fact]
    public void The_primary_contact_wins()
    {
        var merged = ErpContactMerge.Fill(
            [Supplier("A")],
            [
                Contact("zz-ordinary", "A", email: "ordinary@example.com"),
                Contact("aa-primary", "A", email: "primary@example.com", primary: true),
            ]);

        merged[0].Email.Should().Be("primary@example.com");
    }

    [Fact]
    public void With_no_primary_the_order_is_by_name_rather_than_by_arrival()
    {
        ErpSupplierContact[] contacts =
        [
            Contact("beta", "A", email: "beta@example.com"),
            Contact("alpha", "A", email: "alpha@example.com"),
        ];

        ErpContactMerge.Fill([Supplier("A")], contacts)[0].Email.Should().Be("alpha@example.com");
        ErpContactMerge.Fill([Supplier("A")], [.. contacts.Reverse()])[0].Email.Should().Be(
            "alpha@example.com",
            "whichever order the server returns them in, the answer must be the same - an unstable wrong answer "
            + "cannot be noticed");
    }

    [Fact]
    public void Email_and_phone_are_resolved_independently()
    {
        var merged = ErpContactMerge.Fill(
            [Supplier("A", email: "known@example.com")],
            [Contact("c1", "A", email: null, phone: "+963 21 555")]);

        merged[0].Email.Should().Be("known@example.com");
        merged[0].Phone.Should().Be(
            "+963 21 555",
            "a contact with no email may still carry the phone, and dropping it would lose a value that was "
            + "sitting right there");
    }

    [Fact]
    public void A_supplier_with_no_contact_at_all_is_left_alone()
    {
        var merged = ErpContactMerge.Fill(
            [Supplier("Tartous Beverages (seed)")],
            [Contact("c1", "Damascus Supplies Co (seed)", email: "someone@example.com")]);

        merged[0].Email.Should().BeNull("a contact on another supplier is not this supplier's");
        merged.Should().HaveCount(1);
    }

    [Fact]
    public void No_contacts_at_all_changes_nothing()
    {
        var suppliers = new[] { Supplier("A"), Supplier("B", email: "b@example.com") };

        ErpContactMerge.Fill(suppliers, []).Should().BeEquivalentTo(suppliers);
    }
}
