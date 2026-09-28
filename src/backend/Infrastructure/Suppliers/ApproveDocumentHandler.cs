// A reviewer approves one uploaded document.
//
// Simpler than the three-way decision on a whole application, because this is part of the document's own
// lifecycle rather than a separate review flow.
//
// The supplier record is loaded so its NEW version can be read after the save, not because this handler
// edits it. Saving a child marks the root as modified and advances its version, and the new value is only
// readable from a tracked entity, so without that line the endpoint would have nothing to put on the
// response's version header.
//
//
// APPROVING THE REPLACEMENT LIFTS THE SUSPENSION THE EXPIRY CAUSED
//
// The suspension is automatic and rule-based, a job noticing a date had passed, so its reversal should be
// too once the rule's condition is objectively gone.
//
// The human check has already happened: a supplier uploaded a replacement and a reviewer approved it, which
// is this very method's trigger. Requiring a second person to confirm the reinstatement adds no
// information and introduces the worse failure, a supplier who has fixed the problem sitting suspended and
// locked out of tenders until somebody happens to notice.
//
// "Objectively gone" is read narrowly: no award-critical document type on this supplier is left with an
// expired latest version. Not "this document is fine". A supplier suspended for two expiries must not be
// reinstated by fixing one of them, and that is the case this test exists to refuse.
//
// It reactivates only from suspended, and only when the suspension was this rule's. A supplier suspended by
// a person for a reason of their own stays suspended: the last suspension in their audit trail is that person's,
// so this does nothing. Reinstating them would be a document decision overturning a human one.
//
// THE LAST SUSPENSION IS ANY ROW THAT SUSPENDED THE SUPPLIER, not a list of known actions. The list this used to
// read named only the expiry rule and a person, and the ERP sync added two more sources - "no longer in the ERP"
// and "disabled in the ERP" - that it could not see. A supplier suspended by the rule once, reinstated, and later
// suspended by the sync was then reactivated by its next approved document, because the latest row the list could
// see was the old automatic one; the sync read the result as a person's reinstatement and never suspended it again.
// Every suspension writes its target state, so reading that finds whichever source acted last, including any added
// after this.
//
// A SUPPLIER THAT LEFT THE ERP WHILE SUSPENDED IS NOT BROUGHT BACK BY A DOCUMENT. The sync only marks such a
// supplier, because it was out of service already; its expiry suspension is still the last one, but a renewed
// document says nothing about whether Seven Gates still has the company. Reinstating it would make it invitable for
// a night until the sync suspended it again, with a "you are reinstated" message in between. It waits for a person,
// and the sync respects what they decide after its one suspension - see ErpSyncPlan.
//
// The audit row names the replacement document and the reviewer whose approval triggered the
// reinstatement. "Reactivated automatically" and nothing else would leave the next reader unable to tell
// why participation came back, which is the same gap the suspension row was written to close.
//
// And the supplier is told. They were told when participation was removed, and a system that takes the
// trouble to say "you are suspended" then stays silent when it lifts leaves them assuming the worst and not
// bidding.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class ApproveDocumentHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IApproveDocumentHandler
{
    public async Task<ReviewDocumentResult> HandleAsync(string documentCode, CancellationToken ct)
    {
        if (scope.UserId is null)
        {
            return new ReviewDocumentResult.NotFoundOrForbidden();
        }

        var document = await db.SupplierDocuments.FirstOrDefaultAsync(d => d.ReferenceCode == documentCode, ct);
        if (document is null)
        {
            return new ReviewDocumentResult.NotFoundOrForbidden();
        }

        try
        {
            document.Approve(scope.UserId.Value);
        }
        catch (DomainException ex)
        {
            return new ReviewDocumentResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("SupplierDocument", document.Id, "document_approved", scope.UserId, referenceCode: document.ReferenceCode, ct: ct);
        await ReinstateIfTheSuspensionIsOverAsync(db, auditLogger, document, scope.UserId.Value, ct);

        var supplier = await db.Suppliers.FirstAsync(s => s.Id == document.SupplierId, ct);
        await db.SaveChangesAsync(ct);

        return new ReviewDocumentResult.Success(UploadDocumentHandler.ToDto(document), supplier.RowVersion);
    }

    internal static async Task ReinstateIfTheSuspensionIsOverAsync(
        AppDbContext db, IAuditLogger auditLogger, SupplierDocument document, Guid reviewerId, CancellationToken ct)
    {
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == document.SupplierId, ct);
        if (supplier is null || supplier.LifecycleState != SupplierLifecycleState.Suspended) return;
        if (supplier.SyncStatus == SupplierSyncStatus.MarkedRemovedFromErp) return;

        var lastSuspension = await db.AuditLogs.AsNoTracking()
            .Where(a => a.AggregateId == supplier.Id && a.ToState == nameof(SupplierLifecycleState.Suspended))
            .OrderByDescending(a => a.OccurredAt)
            .Select(a => a.Action)
            .FirstOrDefaultAsync(ct);

        if (lastSuspension != "supplier_auto_suspended") return;

        var awardCriticalTypeIds = await db.DocumentTypes.AsNoTracking()
            .Where(t => t.IsAwardCritical)
            .Select(t => t.Id)
            .ToListAsync(ct);

        var stillExpired = await db.SupplierDocuments.AsNoTracking()
            .AnyAsync(d => d.SupplierId == supplier.Id
                           && d.IsLatestVersion
                           && d.State == DocumentState.Expired
                           && awardCriticalTypeIds.Contains(d.DocumentTypeId), ct);

        if (stillExpired) return;

        var reason = "Automatic reinstatement (BRULE-023/D-67): the award-critical document that expired has "
                     + $"been replaced and approved ({document.ReferenceCode}).";

        supplier.Reactivate(reason);

        await auditLogger.LogAsync(
            "Supplier", supplier.Id, "supplier_auto_reinstated", reviewerId,
            referenceCode: supplier.ReferenceCode,
            fromState: nameof(SupplierLifecycleState.Suspended),
            toState: nameof(SupplierLifecycleState.Active),
            reason: reason, ct: ct);

        NotificationOutbox.EnqueueMany(
            db, NotificationTypes.SupplierReinstated,
            await db.Users.Where(u => u.SupplierId == supplier.Id).Select(u => u.Id).ToListAsync(ct),
            $"{NotificationTypes.SupplierReinstated}:{supplier.Id}:{document.Id}",
            new Dictionary<string, string?> { ["supplierCode"] = supplier.ReferenceCode });
    }
}
