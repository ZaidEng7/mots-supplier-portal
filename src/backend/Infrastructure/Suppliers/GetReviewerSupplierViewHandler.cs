// Everything a reviewer needs to see about one application, on one read.
//
// The supplier's own version is lifted to the top of the wrapper so the read can issue a version header,
// which the decision endpoints' preconditions require.
//
// It is read from the record rather than from the nested read model, so the two cannot drift.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using System.Text.Json;
using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Infrastructure.Configuration;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class GetReviewerSupplierViewHandler(AppDbContext db) : IGetReviewerSupplierViewHandler
{
    public async Task<ReviewerSupplierViewDto?> HandleAsync(string referenceCode, CancellationToken ct)
    {
        var supplier = await db.Suppliers.IncludeProfile()
            .FirstOrDefaultAsync(s => s.ReferenceCode == referenceCode, ct);
        if (supplier is null) return null;

        var documents = await ListSupplierDocumentsHandler.BuildAsync(db, supplier.Id, ct);

        var typesById = await db.DocumentTypes.ToDictionaryAsync(t => t.Id, t => t.Code, ct);
        var annotations = await db.SupplierReviewAnnotations
            .Where(a => a.SupplierId == supplier.Id)
            .OrderByDescending(a => a.RequestedAt)
            .Select(a => new ReviewAnnotationDto(
                a.Id, a.RequestedAt, a.Reason, a.FlaggedProfileFields,
                a.FlaggedDocumentTypeIds.Select(id => typesById.GetValueOrDefault(id, id.ToString())).ToList(),
                a.ResolvedAt))
            .ToListAsync(ct);

        var erpSync = new ErpSyncDto(supplier.ExternalId, supplier.SyncStatus.ToString(), supplier.LastSyncedAt);
        return new ReviewerSupplierViewDto(
            SupplierDtoMapper.ToDto(supplier), erpSync, documents, annotations, supplier.RowVersion);
    }
}
