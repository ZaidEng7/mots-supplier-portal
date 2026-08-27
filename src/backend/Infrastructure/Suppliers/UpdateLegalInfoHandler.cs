using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>FEAT-04.2/FR-PROF-002.</summary>
public sealed class UpdateLegalInfoHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IUpdateLegalInfoHandler
{
    public async Task<UpdateProfileResult> HandleAsync(UpdateLegalInfoCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new UpdateProfileResult.NotFoundOrOutOfScope();

        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new UpdateProfileResult.NotFoundOrOutOfScope();

        var isComplianceCritical = await SupplierFieldConfigLookup.IsEnabledAsync(db, FieldConfigCategory.ComplianceRetrigger, "legalInfo", defaultValue: true, ct);

        var stateBefore = supplier.OnboardingState;
        try
        {
            supplier.UpdateLegalInfo(command.LegalNameAr, command.LegalNameEn, command.RegistrationNumber, command.TaxId, command.SupplierType, command.EstablishedOn, isComplianceCritical);
        }
        catch (DomainException ex)
        {
            return new UpdateProfileResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("Supplier", supplier.Id, "legal_info_updated", Guid.NewGuid(), scope.UserId, referenceCode: supplier.ReferenceCode, ct: ct);
        await ComplianceReTrigger.LogIfReTriggeredAsync(db, auditLogger, supplier, stateBefore, "legalInfo", scope.UserId, ct);
        await db.SaveChangesAsync(ct);

        return new UpdateProfileResult.Success(SupplierDtoMapper.ToDto(supplier));
    }
}
