using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Organizations;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// Task #7/Stage B: AppUser.cs's own doc comment has always claimed "SupplierId XOR
/// OrganizationId XOR neither", but until this stage it was convention only - nothing in the
/// database enforced it, the same "comment instead of instrument" pattern flagged elsewhere in
/// this project (MSP-83 register). These tests prove the new CK_app_user_supplier_xor_organization
/// check constraint actually rejects the invalid case against a real Postgres, not that the
/// constraint merely exists in the migration file - and that it does NOT reject the legitimate
/// cases (only one set, or neither set for a platform admin).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AppUserOrganizationConstraintTests(PostgresApiFixture fixture)
{
    private static AppUser NewUser(string email, Guid? supplierId, Guid? organizationId) => new()
    {
        Id = Guid.CreateVersion7(),
        UserName = email,
        Email = email,
        FullName = "Constraint Test",
        EmailConfirmed = true,
        IsActive = true,
        SupplierId = supplierId,
        OrganizationId = organizationId,
    };

    [Fact]
    public async Task A_row_with_both_SupplierId_and_OrganizationId_set_is_rejected_by_the_real_database()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var org = Organization.Create("منظمة الاختبار", "Test Org", OrganizationType.Ministry);
        db.Set<Organization>().Add(org);
        await db.SaveChangesAsync();

        db.Set<AppUser>().Add(NewUser($"xor-{Guid.NewGuid():N}@example.com", supplierId: Guid.CreateVersion7(), organizationId: org.Id));

        var act = async () => await db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>()
            .Where(e => e.InnerException != null && e.InnerException.Message.Contains("CK_app_user_supplier_xor_organization"));
    }

    [Fact]
    public async Task A_row_with_only_OrganizationId_set_is_accepted()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var org = Organization.Create("منظمة الاختبار٢", "Test Org 2", OrganizationType.Hotel);
        db.Set<Organization>().Add(org);
        await db.SaveChangesAsync();

        db.Set<AppUser>().Add(NewUser($"org-only-{Guid.NewGuid():N}@example.com", supplierId: null, organizationId: org.Id));

        var act = async () => await db.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task A_row_with_neither_SupplierId_nor_OrganizationId_set_is_accepted_platform_admin()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Set<AppUser>().Add(NewUser($"neither-{Guid.NewGuid():N}@example.com", supplierId: null, organizationId: null));

        var act = async () => await db.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }
}
