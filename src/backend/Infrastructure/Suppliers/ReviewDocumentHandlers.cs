using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
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
        await db.SaveChangesAsync(ct);

        return new ReviewDocumentResult.Success(UploadDocumentHandler.ToDto(document));
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
        await db.SaveChangesAsync(ct);

        var userId = await db.Users.Where(u => u.SupplierId == document.SupplierId)
            .Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
        if (userId is not null)
        {
            // The filename and the rejection reason are both on the document row, so the job reads
            // them rather than the job store holding them (MSP-89).
            backgroundJobs.Enqueue<EmailJobs>(job => job.SendDocumentRejectedEmailAsync(userId.Value, document.Id, CancellationToken.None));
        }

        return new ReviewDocumentResult.Success(UploadDocumentHandler.ToDto(document));
    }
}
