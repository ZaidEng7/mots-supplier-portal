// An authorised, audited, short-lived link to one tender document.
//
//
// TWO CALLERS, TWO SCOPES, ONE ANSWER FOR A MISS
//
// Buyer staff read their own organization's tenders. An invited supplier reads a tender they hold an
// invitation to, and only once it is published.
//
// Both tests already exist elsewhere and are reused rather than restated, because a second
// implementation of "may this caller see this tender" is a second place for it to be wrong.
//
// The attachment is resolved THROUGH the tender, never by its own identifier. Looking it up by identifier
// and then checking the parent would make the identifier itself the key, which is the classic
// direct-object-reference defect: the guard then passes for anyone who can guess it or is given it.
//
//
// QUARANTINE FIRST
//
// An attachment nothing has scanned is not served. It is scanned now and refused if the scanner objects.
// Rows that predate this carry the unscanned state, so this is also the path that makes them readable again
// once they pass.
//
// The refusal is the same not-found every other miss returns, deliberately. A distinct "this file is
// infected" answer would tell an uploader their malware arrived, which is the one thing worth not
// confirming.
//
//
// WHAT A SIGNED LINK MEANS, SAID PLAINLY
//
// The written security architecture mandates the shape: authorise first, then mint a short-lived signed link
// scoped to that exact object. The five-minute lifetime follows that document's own figure and the
// supplier-document path that already implements it.
//
// The consequence is worth stating. Once minted, the link is a bearer capability: anyone holding it can
// fetch the object until it expires, and the application sees neither the fetch nor the fetcher.
//
// That is the documented design, and it is why the AUTHORISATION is what gets audited here rather than the
// download. A tender document being handed to a bidder is an auditable act in its own right, because it is
// the evidence that every invited supplier had access to the same specification, which is exactly what a
// challenge to a tender asks about.

namespace MotsSupplierPortal.Infrastructure.Rfqs;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Storage;

public sealed class GetRfqAttachmentDownloadUrlHandler(
    AppDbContext db, IScopeContext scope, IFileStorage fileStorage, IAuditLogger auditLogger,
    AttachmentScanner attachmentScanner)
    : IGetRfqAttachmentDownloadUrlHandler
{
    private static readonly TimeSpan UrlLifetime = TimeSpan.FromMinutes(5);

    public async Task<RfqAttachmentDownloadResult> HandleAsync(string rfqReferenceCode, Guid attachmentId, CancellationToken ct)
    {
        var rfq = await LoadReadableRfqAsync(rfqReferenceCode, ct);
        if (rfq is null) return new RfqAttachmentDownloadResult.NotFoundOrForbidden();

        var attachment = rfq.Attachments.FirstOrDefault(a => a.Id == attachmentId);
        if (attachment is null) return new RfqAttachmentDownloadResult.NotFoundOrForbidden();

        var safe = await attachmentScanner.EnsureScannedAsync(
            attachment.ScanState, attachment.StorageKey,
            attachment.MarkScanClean, attachment.MarkScanRejected, ct);

        if (!safe)
        {
            await auditLogger.LogAsync("RfqAttachment", attachment.Id, "rfq_attachment_scan_rejected",
                scope.UserId, referenceCode: rfq.ReferenceCode, ct: ct);
            await db.SaveChangesAsync(ct);
            return new RfqAttachmentDownloadResult.NotFoundOrForbidden();
        }

        var url = await fileStorage.GetSignedDownloadUrlAsync(
            attachment.StorageKey, UrlLifetime, attachment.OriginalFileName, ct);

        await auditLogger.LogAsync("RfqAttachment", attachment.Id, "rfq_attachment_access_granted",
            scope.UserId, referenceCode: rfq.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);

        return new RfqAttachmentDownloadResult.Success(url, attachment.OriginalFileName);
    }

    private async Task<Rfq?> LoadReadableRfqAsync(string referenceCode, CancellationToken ct)
    {
        if (scope.SupplierId is not null)
        {
            var invited = await SupplierRfqLoader.LoadInvitedAsync(db, scope, referenceCode, ct);
            return invited?.Rfq;
        }

        return await db.Rfqs
            .Include(r => r.Attachments)
            .FirstOrDefaultAsync(
                r => r.ReferenceCode == referenceCode && r.OrganizationId == scope.OrganizationId, ct);
    }
}
