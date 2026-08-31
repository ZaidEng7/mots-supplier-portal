using FluentAssertions;
using MotsSupplierPortal.Domain.Organizations;
using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Tests.Unit.Domain;

public class OrganizationTests
{
    private static Organization CreateMinistry() =>
        Organization.Create("وزارة السياحة", "Ministry of Tourism", OrganizationType.Ministry);

    [Fact]
    public void Create_builds_an_organization_with_a_generated_v7_id_and_active_by_default()
    {
        var org = Organization.Create("فندق الاختبار", "Test Hotel", OrganizationType.Hotel, "contact@example.com", "+963900000000");

        org.Id.Should().NotBe(Guid.Empty);
        org.LegalNameAr.Should().Be("فندق الاختبار");
        org.LegalNameEn.Should().Be("Test Hotel");
        org.OrganizationType.Should().Be(OrganizationType.Hotel);
        org.ContactEmail.Should().Be("contact@example.com");
        org.ContactPhone.Should().Be("+963900000000");
        org.IsActive.Should().BeTrue();
        org.SyncStatus.Should().Be(OrganizationSyncStatus.Pending);
        org.OrgUnits.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_blank_Arabic_legal_name(string blank)
    {
        var act = () => Organization.Create(blank, "Test Hotel", OrganizationType.Hotel);
        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_blank_English_legal_name(string blank)
    {
        var act = () => Organization.Create("فندق الاختبار", blank, OrganizationType.Hotel);
        act.Should().Throw<DomainException>();
    }

    /// <summary>OrganizationType is a real C# enum, not a free string - an invalid value cannot
    /// be constructed at all, which the compiler already guarantees. What is worth asserting is
    /// that the three real values round-trip correctly.</summary>
    [Theory]
    [InlineData(OrganizationType.Hotel)]
    [InlineData(OrganizationType.MotBody)]
    [InlineData(OrganizationType.Ministry)]
    public void Create_accepts_every_defined_OrganizationType(OrganizationType type)
    {
        var org = Organization.Create("Test AR", "Test EN", type);
        org.OrganizationType.Should().Be(type);
    }

    [Fact]
    public void IsMinistry_is_true_only_for_the_Ministry_type()
    {
        CreateMinistry().IsMinistry.Should().BeTrue();
        Organization.Create("Test AR", "Test EN", OrganizationType.Hotel).IsMinistry.Should().BeFalse();
        Organization.Create("Test AR", "Test EN", OrganizationType.MotBody).IsMinistry.Should().BeFalse();
    }

    [Fact]
    public void AddOrgUnit_adds_a_root_unit_belonging_to_this_organization()
    {
        var org = CreateMinistry();

        var unit = org.AddOrgUnit("Procurement Committee");

        org.OrgUnits.Should().ContainSingle();
        unit.OrganizationId.Should().Be(org.Id);
        unit.ParentOrgUnitId.Should().BeNull();
    }

    [Fact]
    public void AddOrgUnit_nests_a_child_under_an_existing_unit_in_the_same_organization()
    {
        var org = CreateMinistry();
        var parent = org.AddOrgUnit("Procurement Committee");

        var child = org.AddOrgUnit("Evaluation Sub-Committee", parent.Id);

        child.ParentOrgUnitId.Should().Be(parent.Id);
        org.OrgUnits.Should().HaveCount(2);
    }

    [Fact]
    public void AddOrgUnit_rejects_a_parent_id_that_does_not_belong_to_this_organization()
    {
        var org = CreateMinistry();
        var foreignParentId = Guid.CreateVersion7();

        var act = () => org.AddOrgUnit("Orphan Unit", foreignParentId);

        act.Should().Throw<DomainException>();
        org.OrgUnits.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddOrgUnit_rejects_a_blank_name(string blank)
    {
        var org = CreateMinistry();
        var act = () => org.AddOrgUnit(blank);
        act.Should().Throw<DomainException>();
    }
}
