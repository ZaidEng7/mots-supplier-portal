using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Storage;

namespace MotsSupplierPortal.Infrastructure.Proposals;

/// <summary>The half both callers share: scan, audit, mint. Kept together because the ORDER is the
/// security property - the scan gate runs before a URL exists, not after.</summary>
internal static class ProposalDocumentDownload
{
    private static readonly TimeSpan UrlLifetime = TimeSpan.FromMinutes(5);

    public static async Task<ProposalDocumentDownloadResult> MintAsync(
        AppDbContext db, IScopeContext scope, IFileStorage fileStorage, IAuditLogger auditLogger,
        AttachmentScanner attachmentScanner, Proposal proposal, ProposalDocument document, CancellationToken ct)
    {
        var safe = await attachmentScanner.EnsureScannedAsync(
            document.ScanState, document.StorageKey, document.MarkScanClean, document.MarkScanRejected, ct);

        if (!safe)
        {
            await auditLogger.LogAsync("ProposalDocument", document.Id, "proposal_document_scan_rejected",
                scope.UserId, referenceCode: proposal.ReferenceCode, ct: ct);
            await db.SaveChangesAsync(ct);
            return new ProposalDocumentDownloadResult.NotFoundOrForbidden();
        }

        var url = await fileStorage.GetSignedDownloadUrlAsync(
            document.StorageKey, UrlLifetime, document.OriginalFileName, ct);

        // Who opened which bid document, and when, is the evidence a challenged award turns on.
        await auditLogger.LogAsync("ProposalDocument", document.Id, "proposal_document_access_granted",
            scope.UserId, referenceCode: proposal.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);

        return new ProposalDocumentDownloadResult.Success(url, document.OriginalFileName);
    }
}
