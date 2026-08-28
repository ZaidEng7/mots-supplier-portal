using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>BRULE-009: records T&C acceptance with a version and timestamp - the consent record
/// BRULE-004/BUSINESS-PROCESSES.md's submit gate checks for via Supplier.GetMissingProfileFields().</summary>
/// <summary>MSP-77 note: deliberately NOT subject to FlaggedFieldGuard. Accepting the T&amp;C is a
/// consent action, not a profile-field edit, and it is not a flaggable code in
/// <see cref="MotsSupplierPortal.Domain.Suppliers.ProfileFieldCodes"/>. Guarding it would block a
/// supplier under InfoRequested from satisfying the BRULE-009 submit gate and so from resubmitting
/// at all - a lockout, not a restriction.</summary>
public sealed class AcceptTermsHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IAcceptTermsHandler
{
    public async Task<AcceptTermsResult> HandleAsync(CancellationToken ct)
    {
        if (scope.SupplierId is null)
        {
            return new AcceptTermsResult.NotFoundOrOutOfScope();
        }

        var supplier = await db.Suppliers
            .IncludeProfile()
            .FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);

        if (supplier is null)
        {
            return new AcceptTermsResult.NotFoundOrOutOfScope();
        }

        try
        {
            supplier.AcceptTerms(Supplier.CurrentTermsVersion);
        }
        catch (DomainException ex)
        {
            return new AcceptTermsResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync(
            "Supplier", supplier.Id, "terms_accepted", Guid.NewGuid(), scope.UserId,
            reason: Supplier.CurrentTermsVersion, referenceCode: supplier.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);

        return new AcceptTermsResult.Success(SupplierDtoMapper.ToDto(supplier));
    }
}
