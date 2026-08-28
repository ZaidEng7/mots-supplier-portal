using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>FEAT-04.7/FR-PROF-007.</summary>
public sealed class ManageCategoryLinkHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IManageCategoryLinkHandler
{
    public async Task<ProfileMutationResult> LinkAsync(LinkCategoryCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new ProfileMutationResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new ProfileMutationResult.NotFoundOrOutOfScope();

        var refusal = await FlaggedFieldGuard.RefusalReasonAsync(db, supplier, ProfileFieldCodes.CategoryLink, ct);
        if (refusal is not null) return new ProfileMutationResult.NotEditable(refusal);

        var categoryExists = await db.Categories.AnyAsync(c => c.Code == command.CategoryCode && c.IsActive, ct);
        if (!categoryExists) return new ProfileMutationResult.InvalidState("Unknown or inactive category code.");

        var isComplianceCritical = await SupplierFieldConfigLookup.IsEnabledAsync(db, FieldConfigCategory.ComplianceRetrigger, "categoryLink", defaultValue: true, ct);

        var stateBefore = supplier.OnboardingState;
        CategoryLink? link;
        try
        {
            link = supplier.LinkCategory(command.CategoryCode, isComplianceCritical);
        }
        catch (DomainException ex)
        {
            return new ProfileMutationResult.InvalidState(ex.Message);
        }

        // CategoryLink.Id is client-assigned (Guid.CreateVersion7()), so EF's graph-tracking
        // heuristic would otherwise mark it Modified (no-op UPDATE) instead of Added - track it
        // explicitly. Null means LinkCategory was a no-op (already linked).
        if (link is not null) db.CategoryLinks.Add(link);

        var changes = AuditChangeBuilder.Build(("categoryCode", null, command.CategoryCode));

        await auditLogger.LogAsync("Supplier", supplier.Id, "category_linked", Guid.NewGuid(), scope.UserId, reason: command.CategoryCode, referenceCode: supplier.ReferenceCode, changes: changes, ct: ct);
        await ComplianceReTrigger.LogIfReTriggeredAsync(db, auditLogger, supplier, stateBefore, "categoryLink", scope.UserId, ct);
        await db.SaveChangesAsync(ct);
        return new ProfileMutationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }

    public async Task<ProfileMutationResult> UnlinkAsync(UnlinkCategoryCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new ProfileMutationResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new ProfileMutationResult.NotFoundOrOutOfScope();

        var refusal = await FlaggedFieldGuard.RefusalReasonAsync(db, supplier, ProfileFieldCodes.CategoryLink, ct);
        if (refusal is not null) return new ProfileMutationResult.NotEditable(refusal);

        var isComplianceCritical = await SupplierFieldConfigLookup.IsEnabledAsync(db, FieldConfigCategory.ComplianceRetrigger, "categoryLink", defaultValue: true, ct);

        var stateBefore = supplier.OnboardingState;
        try
        {
            supplier.UnlinkCategory(command.CategoryCode, isComplianceCritical);
        }
        catch (DomainException ex)
        {
            return new ProfileMutationResult.InvalidState(ex.Message);
        }

        var changes = AuditChangeBuilder.Build(("categoryCode", command.CategoryCode, null));

        await auditLogger.LogAsync("Supplier", supplier.Id, "category_unlinked", Guid.NewGuid(), scope.UserId, reason: command.CategoryCode, referenceCode: supplier.ReferenceCode, changes: changes, ct: ct);
        await ComplianceReTrigger.LogIfReTriggeredAsync(db, auditLogger, supplier, stateBefore, "categoryLink", scope.UserId, ct);
        await db.SaveChangesAsync(ct);
        return new ProfileMutationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }
}
