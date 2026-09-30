// The switch on the ERP connection that lets the portal write suppliers to the ERP, and the group it files them under.
//
// THE SWITCH STARTS OFF, and that is asserted on a new connection, because everything else here is about turning it on
// and would pass against a connection that was born writing.
//
// TURNING IT ON WITHOUT A GROUP IS REFUSED, for a missing group, an empty one and one of spaces alike. The ERP refuses
// a supplier with no group, so a switch that turned on without one would fail every create it was turned on for.
//
// TURNING IT OFF IS ALWAYS ALLOWED, with or without a group, and keeps or clears the group as the caller says, so a
// group can be chosen before anything is written.

namespace MotsSupplierPortal.Tests.Unit.Domain;

using FluentAssertions;
using MotsSupplierPortal.Domain.Integration;
using MotsSupplierPortal.Domain.Suppliers;

public sealed class IntegrationConnectionSupplierCreationTests
{
    private static readonly Guid Admin = Guid.Parse("00000000-0000-0000-0000-00000000a001");

    private static IntegrationConnection Erp() =>
        IntegrationConnection.Create(IntegrationConnection.ErpKey, "Seven Gates ERP");

    [Fact]
    public void A_new_connection_writes_no_suppliers_to_the_erp()
    {
        var connection = Erp();

        connection.CreateSuppliersInErp.Should().BeFalse();
        connection.DefaultSupplierGroup.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Turning_supplier_creation_on_without_a_group_is_refused(string? group)
    {
        var connection = Erp();

        var act = () => connection.SetSupplierCreation(true, group, Admin);

        act.Should().Throw<DomainException>().WithMessage("*default ERP supplier group*");
        connection.CreateSuppliersInErp.Should().BeFalse();
        connection.UpdatedByUserId.Should().BeNull("a refused change records nobody as having made it");
    }

    [Fact]
    public void Turning_supplier_creation_on_with_a_group_records_both_and_who_did_it()
    {
        var connection = Erp();
        var before = DateTimeOffset.UtcNow;

        connection.SetSupplierCreation(true, "  Tourism Services - SYP  ", Admin);

        connection.CreateSuppliersInErp.Should().BeTrue();
        connection.DefaultSupplierGroup.Should().Be("Tourism Services - SYP");
        connection.UpdatedByUserId.Should().Be(Admin);
        connection.UpdatedAt.Should().NotBeNull().And.BeOnOrAfter(before);
    }

    [Fact]
    public void Turning_it_off_keeps_or_clears_the_group_as_asked()
    {
        var connection = Erp();
        connection.SetSupplierCreation(true, "Tourism Services - SYP", Admin);

        connection.SetSupplierCreation(false, "Tourism Services - SYP", Admin);

        connection.CreateSuppliersInErp.Should().BeFalse();
        connection.DefaultSupplierGroup.Should().Be("Tourism Services - SYP");

        connection.SetSupplierCreation(false, null, Admin);

        connection.CreateSuppliersInErp.Should().BeFalse();
        connection.DefaultSupplierGroup.Should().BeNull();
    }

    [Fact]
    public void A_group_is_limited_to_what_the_erp_holds()
    {
        var connection = Erp();

        connection.SetSupplierCreation(true, new string('g', IntegrationConnection.SupplierGroupMaxLength), Admin);
        connection.DefaultSupplierGroup.Should().HaveLength(IntegrationConnection.SupplierGroupMaxLength);

        var act = () => connection.SetSupplierCreation(
            true, new string('h', IntegrationConnection.SupplierGroupMaxLength + 1), Admin);

        act.Should().Throw<DomainException>();
        connection.DefaultSupplierGroup.Should().HaveLength(IntegrationConnection.SupplierGroupMaxLength)
            .And.StartWith("g", "the refused name is not stored");
    }
}
