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

namespace MotsSupplierPortal.Infrastructure.Suppliers;

public sealed class GetOwnActiveAnnotationHandler(AppDbContext db, IScopeContext scope) : IGetOwnActiveAnnotationHandler
{
    public async Task<ReviewAnnotationDto?> HandleAsync(CancellationToken ct)
    {
        if (scope.SupplierId is null) return null;

        var annotation = await db.SupplierReviewAnnotations
            .Where(a => a.SupplierId == scope.SupplierId && a.ResolvedAt == null)
            .OrderByDescending(a => a.RequestedAt)
            .FirstOrDefaultAsync(ct);
        if (annotation is null) return null;

        var typesById = await db.DocumentTypes.ToDictionaryAsync(t => t.Id, t => t.Code, ct);
        return new ReviewAnnotationDto(
            annotation.Id, annotation.RequestedAt, annotation.Reason, annotation.FlaggedProfileFields,
            [.. annotation.FlaggedDocumentTypeIds.Select(id => typesById.GetValueOrDefault(id, id.ToString()))],
            annotation.ResolvedAt);
    }
}
