// A system administrator retries the push of a supplier the ERP could not create.
//
// The move itself is Supplier.RetryErpPush: from Failed only, and only for a supplier in service, back to Requested,
// or to Linked when the ERP already has the Supplier record, with the count started again and the next attempt due at
// once. This handler saves it, puts it on the supplier's trail with the administrator as the actor, and enqueues
// SupplierErpPushJob for this supplier after the commit, as approval does, so the push runs straight away rather than
// at the next sweep. Whether that run writes anything is still the job's decision, by the connection's write switch.
//
// THE SUPPLIER IS FOUND BY ITS CODE ACROSS THE REGISTRY, NOT THROUGH AN ORGANISATION. The award's retry loads through
// the caller's organisation, and a system administrator has none, so that loader answers not found for exactly the
// person the permission is for. The push serves the deployment, and the route is gated by admin.integrations.manage,
// which no organisation's role holds.
//
// IT SAVES THROUGH THE RECORD, AS EVERY PERSON'S CHANGE TO A SUPPLIER DOES, SO THE VERSION MOVES. The job's own
// targeted updates move it too. A save that read the push before the retry, such as a reviewer's approval on a page
// opened earlier, is then refused as stale instead of writing the old push state back over the retry. The reviewer's
// view is never cached, so the version is not what makes the page show the retry: the next read does.
//
// THE LAST ERROR IS KEPT, as the domain keeps it, and quoted on the trail, so the row that says a person started the
// push again also says what it had failed on.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Integration.Erp;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class RetryErpPushHandler(
    AppDbContext db,
    IScopeContext scope,
    IAuditLogger auditLogger,
    IBackgroundJobClient backgroundJobs) : IRetryErpPushHandler
{
    public async Task<RetryErpPushResult> HandleAsync(string referenceCode, CancellationToken ct)
    {
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.ReferenceCode == referenceCode, ct);
        if (supplier is null) return new RetryErpPushResult.NotFound();

        var statusBefore = supplier.ErpPushStatus;

        try
        {
            supplier.RetryErpPush(DateTimeOffset.UtcNow);
        }
        catch (DomainException refused)
        {
            return new RetryErpPushResult.Invalid(refused.Message);
        }

        await auditLogger.LogAsync(
            "Supplier",
            supplier.Id,
            "supplier.erp_push_retried",
            scope.UserId,
            fromState: statusBefore.ToString(),
            toState: supplier.ErpPushStatus.ToString(),
            reason: supplier.ErpPushLastError is null
                ? "Started again by a person."
                : $"Started again by a person. It had failed on: {supplier.ErpPushLastError}",
            referenceCode: supplier.ReferenceCode,
            ct: ct);

        await db.SaveChangesAsync(ct);

        var supplierId = supplier.Id;
        backgroundJobs.Enqueue<SupplierErpPushJob>(job => job.PushAsync(supplierId, CancellationToken.None));

        return new RetryErpPushResult.Success(SupplierDtoMapper.ToErpSyncDto(supplier));
    }
}
