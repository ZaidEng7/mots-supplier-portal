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
// WHEN IT REINSTATES IS DECIDED IN AutomaticReinstatement, which the ERP sync also calls: the condition must be
// objectively gone, the last suspension must be the expiry rule's rather than a person's or the sync's, and nothing may
// be marking the supplier as one the ERP no longer wants. This handler only supplies the trigger - the document and
// the reviewer whose approval it was.

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
        if (supplier is null) return;

        await AutomaticReinstatement.TryAsync(
            db,
            auditLogger,
            supplier,
            "Automatic reinstatement (BRULE-023/D-67): the award-critical document that expired has been replaced "
            + $"and approved ({document.ReferenceCode}).",
            reviewerId,
            actorLabel: null,
            $"{NotificationTypes.SupplierReinstated}:{supplier.Id}:{document.Id}",
            ct);
    }
}
