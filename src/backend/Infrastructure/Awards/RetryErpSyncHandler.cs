// An administrator retries a failed send to the external purchasing system.
//
// It only moves the integration status back to requested. The recurring job is what actually sends, so this is
// the manual trigger for somebody who does not want to wait for the next scheduled run.

namespace MotsSupplierPortal.Infrastructure.Awards;

using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Awards;
using MotsSupplierPortal.Application.Comparison;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class RetryErpSyncHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IRetryErpSyncHandler
{
    public async Task<AwardMutationResult> HandleAsync(RetryErpSyncCommand command, CancellationToken ct)
    {
        var loaded = await AwardLoader.LoadScopedAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null || loaded.Value.Award is null) return new AwardMutationResult.NotFoundOrOutOfScope();
        var (rfq, award) = loaded.Value;

        try
        {
            award.RetryErpSync();
        }
        catch (DomainException ex)
        {
            return new AwardMutationResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("Award", award.Id, "award.erp_po_retried", scope.UserId, referenceCode: rfq.ReferenceCode,
            toState: nameof(ErpSyncStatus.Requested), ct: ct);
        await db.SaveChangesAsync(ct);
        return new AwardMutationResult.Success(AwardDtoMapper.ToDto(award, rfq.ReferenceCode, await AwardWinner.CodeAsync(db, award, ct)));
    }
}
