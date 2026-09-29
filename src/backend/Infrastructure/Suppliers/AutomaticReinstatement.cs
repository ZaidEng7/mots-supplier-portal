// Lifting a suspension the expiry rule imposed, once the rule's condition is objectively gone.
//
// Two things ask: a reviewer approving a replacement document (ApproveDocumentHandler, where the reasoning for doing
// this automatically at all is written down), and the ERP sync, when a mark that had been holding this back clears.
// They share one set of rules so the two can never disagree about who may come back.
//
//
// THE RULES
//
// "Objectively gone" is read narrowly: every award-critical document type on this supplier that has expired must now
// have an approved latest version. Not "this document is fine", so a supplier suspended for two expiries is not
// reinstated by fixing one of them. And not "the latest version is no longer expired", which is what this used to
// check: an upload supersedes the expired version at once, so a replacement still waiting for review - or one a
// reviewer rejected - read as fixed. Approving one renewal then reinstated a supplier whose other renewal nobody had
// looked at, and the sync, asking on its own schedule, could reinstate on a replacement nobody had approved at all
// while its audit row said one had been.
//
// The documents are read tracked, so the one a reviewer is approving in this same unit of work counts as approved.
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
// Every suspension writes its target state, so reading that finds whichever source suspended it last - including one
// written earlier in this same unit of work and not saved yet. The sync suspends a supplier returned disabled and then,
// in the same save, may ask this; a database read alone saw only the older expiry row, and lifted the suspension the
// sync had just made.
//
// NOTHING AUTOMATIC BRINGS BACK A SUPPLIER THE ERP NO LONGER WANTS. When the ERP stops returning a supplier, or
// disables it, while the supplier is already suspended, the sync has nothing to suspend and only marks it. Its expiry
// suspension is still the last one, but a renewed document says nothing about whether Seven Gates still wants the
// company. Reinstating it would make it invitable until the sync suspended it again, with a "you are reinstated"
// message in between. While the mark stands it waits.
//
// AND WHEN THE MARK CLEARS, THE SYNC ASKS AGAIN. The mark can come from a read that missed the supplier for a run,
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
using MotsSupplierPortal.Domain.Audit;
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

        var unsavedSuspension = db.ChangeTracker.Entries<AuditLog>()
            .Where(e => e.State == EntityState.Added
                        && e.Entity.AggregateId == supplier.Id
                        && e.Entity.ToState == nameof(SupplierLifecycleState.Suspended))
            .OrderByDescending(e => e.Entity.OccurredAt)
            .Select(e => e.Entity.Action)
            .FirstOrDefault();

        var lastSuspension = unsavedSuspension ?? await db.AuditLogs.AsNoTracking()
            .Where(a => a.AggregateId == supplier.Id && a.ToState == nameof(SupplierLifecycleState.Suspended))
            .OrderByDescending(a => a.OccurredAt)
            .Select(a => a.Action)
            .FirstOrDefaultAsync(ct);

        if (lastSuspension != "supplier_auto_suspended") return false;

        var awardCriticalTypeIds = await db.DocumentTypes.AsNoTracking()
            .Where(t => t.IsAwardCritical)
            .Select(t => t.Id)
            .ToListAsync(ct);

        var awardCritical = await db.SupplierDocuments
            .Where(d => d.SupplierId == supplier.Id && awardCriticalTypeIds.Contains(d.DocumentTypeId))
            .ToListAsync(ct);

        var notYetRenewed = awardCritical
            .Where(d => d.State == DocumentState.Expired)
            .Select(d => d.DocumentTypeId)
            .Distinct()
            .Any(typeId => !awardCritical.Any(d =>
                d.DocumentTypeId == typeId
                && d.IsLatestVersion
                && d.State is (DocumentState.Approved or DocumentState.ExpiringSoon)));

        if (notYetRenewed) return false;

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
