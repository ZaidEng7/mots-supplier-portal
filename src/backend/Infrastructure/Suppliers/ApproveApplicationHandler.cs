// A reviewer approves a registration.
//
// The outbound integration event is written in the same commit as the state change. So approval never waits
// on the external system being up, and the event cannot exist without the approval or the approval without
// the event.
//
// AN APPROVAL THAT ASKS FOR THE SUPPLIER TO BE CREATED IN THE ERP ENQUEUES THAT PUSH, after the commit, as the
// approval email is. Supplier.Approve asks only for a supplier with no ExternalId, and the enqueued run pushes that
// supplier alone. Nothing is written to the ERP inside the approval: the reviewer's answer does not wait on the ERP,
// and a push that fails is retried by SupplierErpPushJob's own sweep. Enqueued after the commit, the run always finds
// the request saved; whether it writes anything is the job's decision, by the ERP connection's write switch.

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
using MotsSupplierPortal.Infrastructure.Integration.Erp;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Infrastructure.Configuration;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class ApproveApplicationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger, IBackgroundJobClient backgroundJobs) : IApproveApplicationHandler
{
    public async Task<ReviewDecisionResult> HandleAsync(string referenceCode, CancellationToken ct)
    {
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.ReferenceCode == referenceCode, ct);
        if (supplier is null) return new ReviewDecisionResult.NotFound();

        var blocking = await DocumentCompletenessEvaluator.GetBlockingRequiredDocumentTypeCodesAsync(db, supplier.Id, ct);

        try { supplier.Approve(blocking); }
        catch (DomainException ex) { return new ReviewDecisionResult.InvalidState(ex.Message); }

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

        var supplierId = supplier.Id;
        if (supplier.ErpPushStatus == SupplierErpPushStatus.Requested) backgroundJobs.Enqueue<SupplierErpPushJob>(job => job.PushAsync(supplierId, CancellationToken.None));

        return new ReviewDecisionResult.Success(SupplierDtoMapper.ToDto(supplier));
    }
}
