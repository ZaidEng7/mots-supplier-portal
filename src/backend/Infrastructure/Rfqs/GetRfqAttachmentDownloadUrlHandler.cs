using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Rfqs;

/// <summary>
/// T3-01: an authorized, audited, time-limited URL for one RFQ attachment.
///
/// <para><b>Two callers, two different scopes, one answer for a miss.</b> Buyer staff read their own
/// organization's RFQs; an invited supplier reads an RFQ they hold an invitation to, and only once
/// it is Published - both predicates already exist and are reused rather than restated, because a
/// second implementation of "may this caller see this RFQ" is a second place for it to be wrong.</para>
///
/// <para>The attachment is resolved THROUGH the RFQ, never by its own id. Looking it up by id and
/// then checking the parent would make the id itself the key, which is the classic direct-object
/// read defect: the guard passes for anyone who can guess or is given the id.</para>
///
/// <para>SECURITY-ARCHITECTURE.md §4.2 mandates the shape: authorize first, then mint a short-lived
/// signed URL scoped to that exact object. The 5-minute expiry follows the document's own
/// [ASSUMPTION] figure and the supplier-document path that already implements it. The consequence is
/// worth stating: once minted, the URL is a bearer capability - anyone holding it can fetch that
/// object until it expires, and the application sees neither the fetch nor the fetcher. That is the
/// documented design, and it is why the AUTHORIZATION is audited here rather than the download.</para>
/// </summary>
public sealed class GetRfqAttachmentDownloadUrlHandler(
    AppDbContext db, IScopeContext scope, IFileStorage fileStorage, IAuditLogger auditLogger)
    : IGetRfqAttachmentDownloadUrlHandler
{
    private static readonly TimeSpan UrlLifetime = TimeSpan.FromMinutes(5);

    public async Task<RfqAttachmentDownloadResult> HandleAsync(string rfqReferenceCode, Guid attachmentId, CancellationToken ct)
    {
        var rfq = await LoadReadableRfqAsync(rfqReferenceCode, ct);
        if (rfq is null) return new RfqAttachmentDownloadResult.NotFoundOrForbidden();

        var attachment = rfq.Attachments.FirstOrDefault(a => a.Id == attachmentId);
        if (attachment is null) return new RfqAttachmentDownloadResult.NotFoundOrForbidden();

        var url = await fileStorage.GetSignedDownloadUrlAsync(
            attachment.StorageKey, UrlLifetime, attachment.OriginalFileName, ct);

        // A tender document being handed to a bidder is an auditable act - it is the evidence that
        // every invited supplier had access to the same specification, which is exactly what a
        // challenge to a tender asks about.
        await auditLogger.LogAsync("RfqAttachment", attachment.Id, "rfq_attachment_access_granted",
            scope.UserId, referenceCode: rfq.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);

        return new RfqAttachmentDownloadResult.Success(url, attachment.OriginalFileName);
    }

    /// <summary>
    /// The RFQ, if this caller may read it at all - by organization for staff, by invitation for a
    /// supplier. Null covers both misses and an RFQ that does not exist.
    /// </summary>
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
