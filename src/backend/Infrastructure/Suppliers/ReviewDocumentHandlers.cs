using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>FEAT-05.4/FR-DOC-005: document-level approve/reject - simpler than the application-level
/// three-way decision (STORY-03.2.1/03.3.1), part of the document lifecycle rather than a separate
/// review flow.</summary>
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

        // P12 item 26: the supplier root is loaded so its NEW version can be read after the save, not
        // because this handler edits it. Saving a child marks the root Modified and advances its version
        // (AppDbContext.BumpTouchedVersionedRoots), and the value is only readable from a tracked entity -
        // so without this line the endpoint would have nothing to put on the ETag.
        var supplier = await db.Suppliers.FirstAsync(s => s.Id == document.SupplierId, ct);
        await db.SaveChangesAsync(ct);

        return new ReviewDocumentResult.Success(UploadDocumentHandler.ToDto(document), supplier.RowVersion);
    }

    /// <summary>
    /// D-67/BRULE-023: approving the replacement lifts the suspension the expiry caused.
    ///
    /// <para><b>Why this is automatic.</b> The suspension is automatic and rule-based - a job noticed a date
    /// had passed - so its reversal should be too, once the rule's condition is objectively gone. The human
    /// check has already happened: a supplier uploaded a replacement and a REVIEWER approved it, which is
    /// this method's own trigger. Requiring a second person to then confirm the reinstatement adds no
    /// information and introduces the worse failure - a supplier who has fixed the problem sitting
    /// suspended, locked out of tenders, until somebody happens to notice.</para>
    ///
    /// <para><b>What "objectively gone" means, narrowly.</b> No award-critical document type on this
    /// supplier is left with an expired latest version. Not "this document is fine" - a supplier suspended
    /// for two expiries must not be reinstated by fixing one of them, and that is the case this predicate
    /// exists to refuse.</para>
    ///
    /// <para><b>What it will not do.</b> It reactivates only from Suspended, and only when the suspension
    /// was this rule's. A supplier suspended by a person for a reason of their own stays suspended: their
    /// audit trail carries no supplier_auto_suspended row, so the last-suspension check below finds
    /// nothing and this method does nothing. Reinstating them would be a document decision overturning a
    /// human one.</para>
    /// </summary>
    internal static async Task ReinstateIfTheSuspensionIsOverAsync(
        AppDbContext db, IAuditLogger auditLogger, SupplierDocument document, Guid reviewerId, CancellationToken ct)
    {
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == document.SupplierId, ct);
        if (supplier is null || supplier.LifecycleState != SupplierLifecycleState.Suspended) return;

        // Was this suspension BRULE-023's? The audit trail is the record of who suspended them and why, and
        // an automatic reinstatement may only undo an automatic suspension.
        var lastSuspension = await db.AuditLogs.AsNoTracking()
            .Where(a => a.AggregateId == supplier.Id
                        && (a.Action == "supplier_auto_suspended" || a.Action == "supplier_suspended"))
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

        // Named in the audit row: the replacement document and the reviewer whose approval triggered this.
        // "Reactivated automatically" with nothing else would leave the next reader unable to tell WHY
        // participation came back, which is the same gap the suspension row was written to close.
        await auditLogger.LogAsync(
            "Supplier", supplier.Id, "supplier_auto_reinstated", reviewerId,
            referenceCode: supplier.ReferenceCode,
            fromState: nameof(SupplierLifecycleState.Suspended),
            toState: nameof(SupplierLifecycleState.Active),
            reason: reason, ct: ct);

        // And the supplier is told. They were told when participation was removed; a system that takes the
        // trouble to say "you are suspended" and stays silent when it lifts leaves them assuming the worst
        // and not bidding.
        NotificationOutbox.EnqueueMany(
            db, NotificationTypes.SupplierReinstated,
            await db.Users.Where(u => u.SupplierId == supplier.Id).Select(u => u.Id).ToListAsync(ct),
            $"{NotificationTypes.SupplierReinstated}:{supplier.Id}:{document.Id}",
            new Dictionary<string, string?> { ["supplierCode"] = supplier.ReferenceCode });
    }
}

public sealed class RejectDocumentHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger, IBackgroundJobClient backgroundJobs) : IRejectDocumentHandler
{
    public async Task<ReviewDocumentResult> HandleAsync(string documentCode, string reason, CancellationToken ct)
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
            document.Reject(scope.UserId.Value, reason);
        }
        catch (DomainException ex)
        {
            return new ReviewDocumentResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("SupplierDocument", document.Id, "document_rejected", scope.UserId, referenceCode: document.ReferenceCode, reason: reason, ct: ct);

        // See the approve handler: tracked so the bumped version can be read back for the ETag.
        var supplier = await db.Suppliers.FirstAsync(s => s.Id == document.SupplierId, ct);
        await db.SaveChangesAsync(ct);

        var userId = await db.Users.Where(u => u.SupplierId == document.SupplierId)
            .Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
        if (userId is not null)
        {
            // The filename and the rejection reason are both on the document row, so the job reads
            // them rather than the job store holding them (MSP-89).
            backgroundJobs.Enqueue<EmailJobs>(job => job.SendDocumentRejectedEmailAsync(userId.Value, document.Id, CancellationToken.None));
        }

        return new ReviewDocumentResult.Success(UploadDocumentHandler.ToDto(document), supplier.RowVersion);
    }
}
