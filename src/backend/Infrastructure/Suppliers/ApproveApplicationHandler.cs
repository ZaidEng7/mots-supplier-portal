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

public sealed class ApproveApplicationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger, IBackgroundJobClient backgroundJobs) : IApproveApplicationHandler
{
    public async Task<ReviewDecisionResult> HandleAsync(string referenceCode, CancellationToken ct)
    {
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.ReferenceCode == referenceCode, ct);
        if (supplier is null) return new ReviewDecisionResult.NotFound();

        var blocking = await DocumentCompletenessEvaluator.GetBlockingRequiredDocumentTypeCodesAsync(db, supplier.Id, ct);

        try { supplier.Approve(blocking); }
        catch (DomainException ex) { return new ReviewDecisionResult.InvalidState(ex.Message); }

        // FEAT-03.5: Outbox event written in the SAME SaveChangesAsync transaction as the state
        // change - approval never blocks on ERP being up, and the event is guaranteed atomic
        // with the approval (docs/architecture/DOMAIN-MODEL.md §5.3).
        var payload = JsonSerializer.Serialize(new
        {
            supplierId = supplier.Id,
            referenceCode = supplier.ReferenceCode,
            displayNameAr = supplier.DisplayNameAr,
            displayNameEn = supplier.DisplayNameEn,
            approvedAt = DateTimeOffset.UtcNow,
        });
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "SupplierApproved",
            PayloadJson = payload,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await auditLogger.LogAsync("Supplier", supplier.Id, "application_approved", scope.UserId, toState: supplier.OnboardingState.ToString(), referenceCode: supplier.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);

        var userId = await ReviewerNotify.GetPrimaryUserIdAsync(db, supplier.Id, ct);
        if (userId is not null) backgroundJobs.Enqueue<EmailJobs>(job => job.SendApplicationApprovedEmailAsync(userId.Value, CancellationToken.None));

        return new ReviewDecisionResult.Success(SupplierDtoMapper.ToDto(supplier));
    }
}
