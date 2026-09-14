// Withdrawing a catalogue entry from future buyer searches.
//
// It hides the entry and keeps the row and its history. Never a delete, because a buyer who saw it last
// month should still find the record of what they saw.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

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
