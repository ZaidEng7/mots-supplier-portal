using FluentAssertions;
using MotsSupplierPortal.Domain.Organizations;
using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Tests.Unit.Domain;

public class SupplierOrgLinkTests
{
    [Fact]
    public void Create_builds_a_link_between_the_given_supplier_and_organization()
    {
        var supplierId = Guid.CreateVersion7();
        var organizationId = Guid.CreateVersion7();

        var link = SupplierOrgLink.Create(supplierId, organizationId);

        link.Id.Should().NotBe(Guid.Empty);
        link.SupplierId.Should().Be(supplierId);
        link.OrganizationId.Should().Be(organizationId);
    }

    [Fact]
    public void Create_rejects_an_empty_SupplierId()
    {
        var act = () => SupplierOrgLink.Create(Guid.Empty, Guid.CreateVersion7());
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_rejects_an_empty_OrganizationId()
    {
        var act = () => SupplierOrgLink.Create(Guid.CreateVersion7(), Guid.Empty);
        act.Should().Throw<DomainException>();
    }
}
