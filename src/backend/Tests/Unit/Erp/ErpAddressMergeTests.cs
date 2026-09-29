// Choosing which of a supplier's ERP addresses the portal takes.
//
// THE ORDER IS THE TEST, and each step is asserted against the one below it: the supplier's own primary-address
// field beats an address ticked primary, which beats a billing address, which beats alphabetical order. The last
// case is the control - two unmarked addresses must give the same answer every time, whichever the ERP sent first.

namespace MotsSupplierPortal.Tests.Unit.Erp;

using FluentAssertions;
using MotsSupplierPortal.Application.Integration;
using MotsSupplierPortal.Infrastructure.Integration.Erp;

public sealed class ErpAddressMergeTests
{
    private static ErpSupplier Supplier(string id, string? primaryAddress = null) =>
        new(id, id, "Local", "Company", null, "Syria", null, null, false, "SYP", primaryAddress, null,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private static ErpSupplierAddressRow Row(
        string name, string supplier, bool primary = false, string type = "Shipping") =>
        new(name, supplier, name + " street", null, "Damascus", "Syria", primary, type);

    [Fact]
    public void The_suppliers_own_primary_address_field_wins()
    {
        var merged = ErpAddressMerge.Fill(
            [Supplier("A", primaryAddress: "A-Named")],
            [Row("A-Ticked", "A", primary: true, type: "Billing"), Row("A-Named", "A")]);

        merged[0].Address!.Line1.Should().Be("A-Named street");
    }

    [Fact]
    public void An_address_ticked_primary_beats_a_billing_one()
    {
        var merged = ErpAddressMerge.Fill(
            [Supplier("A")],
            [Row("A-Billing", "A", type: "Billing"), Row("A-Ticked", "A", primary: true)]);

        merged[0].Address!.Line1.Should().Be("A-Ticked street");
    }

    [Fact]
    public void A_billing_address_beats_an_unmarked_one()
    {
        var merged = ErpAddressMerge.Fill(
            [Supplier("A")],
            [Row("A-1", "A"), Row("A-2", "A", type: "Billing")]);

        merged[0].Address!.Line1.Should().Be("A-2 street");
    }

    [Fact]
    public void Unmarked_addresses_give_the_same_answer_whatever_order_they_arrive_in()
    {
        var one = ErpAddressMerge.Fill([Supplier("A")], [Row("A-2", "A"), Row("A-1", "A")]);
        var other = ErpAddressMerge.Fill([Supplier("A")], [Row("A-1", "A"), Row("A-2", "A")]);

        one[0].Address.Should().Be(other[0].Address);
        one[0].Address!.Line1.Should().Be("A-1 street");
    }

    [Fact]
    public void A_supplier_with_no_address_keeps_none()
    {
        ErpAddressMerge.Fill([Supplier("A"), Supplier("B")], [Row("B-1", "B")])[0].Address.Should().BeNull();
    }

    [Fact]
    public void A_disabled_address_is_never_chosen()
    {
        var merged = ErpAddressMerge.Fill(
            [Supplier("A")],
            [Row("A-Billing", "A", type: "Billing") with { Disabled = true }, Row("A-Billing-1", "A", type: "Billing")]);

        merged[0].Address!.Line1.Should().Be(
            "A-Billing-1 street", "the old address sorts first by name, and filing the supplier there puts it where it left");
    }
}
