// How a sign-in account maps to its table.
//
//
// THE CONSTRAINT: A SUPPLIER, OR AN ORGANIZATION, OR NEITHER
//
// At most one of the two may be set. Neither is still allowed, because that is a platform administrator,
// so this is an at-most-one rule rather than a strict either-or.
//
// It was a convention recorded in prose until it became this constraint. Every existing row was checked
// against real data first rather than trusted from a report: forty-two users, none with an organization,
// thirty-nine with a supplier, three with neither. So it was a clean addition with nothing to reconcile.
//
//
// WHY LOSING AN ORGANIZATION SETS THE COLUMN EMPTY RATHER THAN DELETING THE USER
//
// Deleting an organization is not a real flow yet. When it becomes one, a member of staff losing their
// organization is the right outcome, rather than that person being silently deleted along with it.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Organizations;

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> entity)
    {
        entity.ToTable("app_user", "identity", t =>
        {
            t.HasCheckConstraint("CK_app_user_supplier_xor_organization", "\"SupplierId\" IS NULL OR \"OrganizationId\" IS NULL");
        });
        entity.Property(u => u.FullName).HasMaxLength(200).IsRequired();
        entity.HasIndex(u => u.SupplierId);
        entity.HasIndex(u => u.OrganizationId);
        entity.HasIndex(u => u.OrgUnitId);
        entity.HasOne<Organization>().WithMany().HasForeignKey(u => u.OrganizationId).OnDelete(DeleteBehavior.SetNull);
        entity.HasOne<OrgUnit>().WithMany().HasForeignKey(u => u.OrgUnitId).OnDelete(DeleteBehavior.SetNull);
    }
}
