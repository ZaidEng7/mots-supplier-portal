// Lifting a suspension the expiry rule imposed, once the rule's condition is objectively gone.
//
// Two things ask: a reviewer approving a replacement document (ApproveDocumentHandler, where the reasoning for doing
// this automatically at all is written down), and the ERP sync, when a mark that had been holding this back clears.
// They share one set of rules so the two can never disagree about who may come back.
//
//
// THE RULES
//
// "Objectively gone" is read narrowly: no award-critical document type on this supplier is left with an expired
// latest version. Not "this document is fine". A supplier suspended for two expiries must not be reinstated by fixing
// one of them.
//
// It reactivates only from suspended, and only when the suspension was this rule's. A supplier suspended by a person
// for a reason of their own stays suspended: the last suspension in their audit trail is that person's, so this does
// nothing. Reinstating them would be a document decision overturning a human one.
//
// THE LAST SUSPENSION IS ANY ROW THAT SUSPENDED THE SUPPLIER, not a list of known actions. The list this used to read
// named only the expiry rule and a person, and the ERP sync added two more sources - "no longer in the ERP" and
// "disabled in the ERP" - that it could not see. A supplier suspended by the rule once, reinstated, and later
// suspended by the sync was then reactivated by its next approved document, because the latest row the list could
// see was the old automatic one; the sync read the result as a person's reinstatement and never suspended it again.
// Every suspension writes its target state, so reading that finds whichever source suspended it last.
//
// NOTHING AUTOMATIC BRINGS BACK A SUPPLIER THE ERP NO LONGER WANTS. When the ERP stops returning a supplier, or
// disables it, while the supplier is already suspended, the sync has nothing to suspend and only marks it. Its expiry
// suspension is still the last one, but a renewed document says nothing about whether Seven Gates still wants the
// company. Reinstating it would make it invitable until the sync suspended it again, with a "you are reinstated"
// message in between. While the mark stands it waits.
//
// AND WHEN THE MARK CLEARS, THE SYNC ASKS AGAIN. The mark can come from a read that missed the supplier for a night,
// and a reviewer may approve the renewal in that window. Without a second look the approval would have been the only
// trigger and it had already passed, leaving a supplier who fixed the problem, and whom the ERP still has, locked out
// of tenders until somebody noticed - the worse failure this rule exists to avoid. So the sync calls this as soon as
// the ERP returns or re-enables a marked supplier.
//
// The audit row names what triggered it and who, because "reactivated automatically" and nothing else would leave the
// next reader unable to tell why participation came back. And the supplier is told: they were told when it was
// removed, and silence when it lifts leaves them assuming the worst and not bidding.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Infrastructure.Persistence;

internal static class AutomaticReinstatement
{
    public static async Task<bool> TryAsync(
        AppDbContext db,
        IAuditLogger auditLogger,
        Supplier supplier,
        string reason,
        Guid? actorUserId,
        string? actorLabel,
        string notificationKey,
        CancellationToken ct)
    {
        if (supplier.LifecycleState != SupplierLifecycleState.Suspended) return false;
        if (supplier.IsMarkedAsUnwantedByErp) return false;

        var lastSuspension = await db.AuditLogs.AsNoTracking()
            .Where(a => a.AggregateId == supplier.Id && a.ToState == nameof(SupplierLifecycleState.Suspended))
            .OrderByDescending(a => a.OccurredAt)
            .Select(a => a.Action)
            .FirstOrDefaultAsync(ct);

        if (lastSuspension != "supplier_auto_suspended") return false;

        var awardCriticalTypeIds = await db.DocumentTypes.AsNoTracking()
            .Where(t => t.IsAwardCritical)
            .Select(t => t.Id)
            .ToListAsync(ct);

        var stillExpired = await db.SupplierDocuments.AsNoTracking()
            .AnyAsync(d => d.SupplierId == supplier.Id
                           && d.IsLatestVersion
                           && d.State == DocumentState.Expired
                           && awardCriticalTypeIds.Contains(d.DocumentTypeId), ct);

        if (stillExpired) return false;

        supplier.Reactivate(reason);

        await auditLogger.LogAsync(
            "Supplier", supplier.Id, "supplier_auto_reinstated", actorUserId,
            actorLabel: actorLabel,
            referenceCode: supplier.ReferenceCode,
            fromState: nameof(SupplierLifecycleState.Suspended),
            toState: nameof(SupplierLifecycleState.Active),
            reason: reason, ct: ct);

        NotificationOutbox.EnqueueMany(
            db, NotificationTypes.SupplierReinstated,
            await db.Users.Where(u => u.SupplierId == supplier.Id).Select(u => u.Id).ToListAsync(ct),
            notificationKey,
            new Dictionary<string, string?> { ["supplierCode"] = supplier.ReferenceCode });

        return true;
    }
}
