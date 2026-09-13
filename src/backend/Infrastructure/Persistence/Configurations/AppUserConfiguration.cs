using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Domain.Organizations;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.ReferenceData;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> entity)
    {
        entity.ToTable("app_user", "identity", t =>
        {
            // Task #7/Stage B: SupplierId XOR OrganizationId XOR neither (AppUser.cs's own
            // doc comment, previously convention-only). "Neither" (platform admin) stays
            // allowed - this is NAND (at most one set), not strict XOR. Every existing row
            // has OrganizationId = null today (re-verified against real local data before
            // writing this, not trusted from Stage A's report: 42 users, 0 with
            // OrganizationId, 39 with SupplierId, 3 with neither), so this is a clean
            // additive constraint with nothing to reconcile.
            t.HasCheckConstraint("CK_app_user_supplier_xor_organization", "\"SupplierId\" IS NULL OR \"OrganizationId\" IS NULL");
        });
        entity.Property(u => u.FullName).HasMaxLength(200).IsRequired();
        entity.HasIndex(u => u.SupplierId);
        entity.HasIndex(u => u.OrganizationId);
        entity.HasIndex(u => u.OrgUnitId);
        // SetNull, not Cascade/Restrict: deleting an Organization is not yet a real flow
        // (Stage C+), but when it becomes one, a back-office user losing their org
        // assignment is the right default - not being silently deleted along with the
        // Organization row.
        entity.HasOne<Organization>().WithMany().HasForeignKey(u => u.OrganizationId).OnDelete(DeleteBehavior.SetNull);
        entity.HasOne<OrgUnit>().WithMany().HasForeignKey(u => u.OrgUnitId).OnDelete(DeleteBehavior.SetNull);
    }
}
