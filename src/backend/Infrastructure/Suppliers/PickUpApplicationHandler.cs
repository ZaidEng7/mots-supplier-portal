// A reviewer starts work on a submitted application, moving it into review.

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

public sealed class PickUpApplicationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IPickUpApplicationHandler
{
    public async Task<ReviewDecisionResult> HandleAsync(string referenceCode, CancellationToken ct)
    {
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.ReferenceCode == referenceCode, ct);
        if (supplier is null) return new ReviewDecisionResult.NotFound();

        try { supplier.PickUpForReview(); }
        catch (DomainException ex) { return new ReviewDecisionResult.InvalidState(ex.Message); }

        await auditLogger.LogAsync("Supplier", supplier.Id, "application_picked_up", scope.UserId, toState: supplier.OnboardingState.ToString(), referenceCode: supplier.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ReviewDecisionResult.Success(SupplierDtoMapper.ToDto(supplier));
    }
}
