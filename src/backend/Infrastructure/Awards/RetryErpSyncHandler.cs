// An administrator retries a failed send to the external purchasing system.
//
// It only moves the integration status back to requested. The recurring job is what actually sends, so this is
// the manual trigger for somebody who does not want to wait for the next scheduled run.
//
// THE PLATFORM ADMINISTRATOR FINDS THE AWARD ACROSS THE REGISTRY, NOT THROUGH AN ORGANISATION. The shared loader looks
// the tender up inside the caller's organisation, and the system administrator, the only role that holds
// integration.retry by default, belongs to none, so the retry answered not found to exactly the person the Operations
// page offers it to, on an award the same page had just listed. A caller with neither a supplier nor an organisation who
// holds integration.retry is therefore served across the registry, as the supplier push's retry is. Anybody with an
// organisation keeps the scoped load, so a deployment that grants integration.retry to an organisation's role still
// retries only that organisation's awards, and a supplier's account never takes the wider path whatever it is granted.

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
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class RetryErpSyncHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IRetryErpSyncHandler
{
    public async Task<AwardMutationResult> HandleAsync(RetryErpSyncCommand command, CancellationToken ct)
    {
        var loaded = ServesThePlatform()
            ? await LoadAcrossTheRegistryAsync(command.RfqReferenceCode, ct)
            : await AwardLoader.LoadScopedAsync(db, scope, command.RfqReferenceCode, ct);
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

    private bool ServesThePlatform() =>
        scope.SupplierId is null && scope.OrganizationId is null && scope.HasPermission(Permissions.IntegrationRetry);

    private async Task<(Rfq Rfq, Award? Award)?> LoadAcrossTheRegistryAsync(string rfqReferenceCode, CancellationToken ct)
    {
        var rfq = await db.Rfqs.FirstOrDefaultAsync(r => r.ReferenceCode == rfqReferenceCode, ct);
        if (rfq is null) return null;
        var award = await db.Awards.IncludeAll().FirstOrDefaultAsync(a => a.RfqId == rfq.Id, ct);
        return (rfq, award);
    }
}
