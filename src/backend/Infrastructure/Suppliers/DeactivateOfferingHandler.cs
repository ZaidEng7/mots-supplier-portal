using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>FEAT-06.1 AC2: deactivation hides the offering from future buyer discovery but keeps
/// the row (and its history) - never a delete.</summary>
public sealed class DeactivateOfferingHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IDeactivateOfferingHandler
{
    public async Task<OfferingMutationResult> HandleAsync(Guid offeringId, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new OfferingMutationResult.NotFoundOrOutOfScope();

        var offering = await db.Offerings.FirstOrDefaultAsync(o => o.Id == offeringId && o.SupplierId == scope.SupplierId, ct);
        if (offering is null) return new OfferingMutationResult.NotFoundOrOutOfScope();

        offering.IsActive = false;

        await auditLogger.LogAsync("Offering", offering.Id, "offering_deactivated", scope.UserId, ct: ct);
        await db.SaveChangesAsync(ct);

        return new OfferingMutationResult.Success(OfferingDtoMapper.ToDto(offering));
    }
}
