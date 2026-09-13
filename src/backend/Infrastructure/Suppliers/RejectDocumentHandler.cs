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
