// Recording that a supplier accepted the terms and conditions, with the version and the moment.
//
// This is the consent record the submit gate checks for through the supplier's own missing-fields list.
//
// It is deliberately NOT subject to the flagged-field guard. Accepting the terms is a consent action rather
// than a profile edit, and it is not a flaggable field code at all.
//
// Guarding it would stop a supplier under an information request from satisfying the submit gate, and so
// from resubmitting at all. That is a lockout rather than a restriction.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

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
            "Supplier", supplier.Id, "terms_accepted", scope.UserId,
            reason: Supplier.CurrentTermsVersion, referenceCode: supplier.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);

        return new AcceptTermsResult.Success(SupplierDtoMapper.ToDto(supplier));
    }
}
