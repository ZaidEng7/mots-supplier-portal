// Claiming and releasing the categories a supplier says it can supply.
//
// The category has to exist and be active. Whether a category change sends an approved supplier back for
// review is an administrator's switch rather than a constant, read per call.
//
// A new link is added to the tracked set explicitly, because its identifier is assigned by us rather than by
// the database and the graph-tracking heuristic would otherwise take it for an existing row. No link at all
// means the claim was already recorded and the domain did nothing.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

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

        CategoryLink? link;
        bool reTriggered;
        try
        {
            (link, reTriggered) = supplier.LinkCategory(command.CategoryCode, isComplianceCritical);
        }
        catch (DomainException ex)
        {
            return new ProfileMutationResult.InvalidState(ex.Message);
        }

        if (link is not null) db.CategoryLinks.Add(link);

        var changes = AuditChangeBuilder.Build(("categoryCode", null, command.CategoryCode));

        await auditLogger.LogAsync("Supplier", supplier.Id, "category_linked", scope.UserId, reason: command.CategoryCode, referenceCode: supplier.ReferenceCode, changes: changes, ct: ct);
        await ComplianceReTrigger.LogIfReTriggeredAsync(db, auditLogger, supplier, reTriggered, "categoryLink", scope.UserId, ct);
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

        bool reTriggered;
        try
        {
            reTriggered = supplier.UnlinkCategory(command.CategoryCode, isComplianceCritical);
        }
        catch (DomainException ex)
        {
            return new ProfileMutationResult.InvalidState(ex.Message);
        }

        var changes = AuditChangeBuilder.Build(("categoryCode", command.CategoryCode, null));

        await auditLogger.LogAsync("Supplier", supplier.Id, "category_unlinked", scope.UserId, reason: command.CategoryCode, referenceCode: supplier.ReferenceCode, changes: changes, ct: ct);
        await ComplianceReTrigger.LogIfReTriggeredAsync(db, auditLogger, supplier, reTriggered, "categoryLink", scope.UserId, ct);
        await db.SaveChangesAsync(ct);
        return new ProfileMutationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }
}
