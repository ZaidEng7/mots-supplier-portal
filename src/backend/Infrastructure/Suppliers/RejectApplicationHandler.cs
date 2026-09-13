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

public sealed class RejectApplicationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger, IBackgroundJobClient backgroundJobs) : IRejectApplicationHandler
{
    public async Task<ReviewDecisionResult> HandleAsync(string referenceCode, string reason, CancellationToken ct)
    {
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.ReferenceCode == referenceCode, ct);
        if (supplier is null) return new ReviewDecisionResult.NotFound();

        try { supplier.Reject(reason); }
        catch (DomainException ex) { return new ReviewDecisionResult.InvalidState(ex.Message); }

        await auditLogger.LogAsync("Supplier", supplier.Id, "application_rejected", scope.UserId, toState: supplier.OnboardingState.ToString(), reason: reason, referenceCode: supplier.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);

        var userId = await ReviewerNotify.GetPrimaryUserIdAsync(db, supplier.Id, ct);
        if (userId is not null) backgroundJobs.Enqueue<EmailJobs>(job => job.SendApplicationRejectedEmailAsync(userId.Value, reason, CancellationToken.None));

        return new ReviewDecisionResult.Success(SupplierDtoMapper.ToDto(supplier));
    }
}
