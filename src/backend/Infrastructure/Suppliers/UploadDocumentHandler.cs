using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Storage;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>
/// FR-DOC-002/STORY-05.2.1: validates type/size/magic-bytes, stores to the quarantine prefix, and
/// enqueues the AV scan job - the document is NOT yet downloadable or completeness-satisfying
/// until the scan comes back clean (docs/security/SECURITY-ARCHITECTURE.md §4.1).
/// </summary>
public sealed class UploadDocumentHandler(
    AppDbContext db,
    IScopeContext scope,
    IFileStorage fileStorage,
    IAuditLogger auditLogger,
    IBackgroundJobClient backgroundJobs) : IUploadDocumentHandler
{
    public async Task<UploadDocumentResult> HandleAsync(UploadDocumentCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null || scope.UserId is null)
        {
            return new UploadDocumentResult.NotFoundOrOutOfScope();
        }

        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null)
        {
            return new UploadDocumentResult.NotFoundOrOutOfScope();
        }

        if (supplier.OnboardingState is not (SupplierOnboardingState.EmailVerified or SupplierOnboardingState.ProfileInProgress or SupplierOnboardingState.InfoRequested))
        {
            return new UploadDocumentResult.NotEditable(
                $"Cannot upload documents from state '{supplier.OnboardingState}'.");
        }

        var documentType = await db.DocumentTypes.FirstOrDefaultAsync(t => t.Id == command.DocumentTypeId && t.IsActive, ct);
        if (documentType is null)
        {
            return new UploadDocumentResult.InvalidDocumentType();
        }

        if (supplier.OnboardingState == SupplierOnboardingState.InfoRequested)
        {
            var activeAnnotation = await db.SupplierReviewAnnotations
                .Where(a => a.SupplierId == supplier.Id && a.ResolvedAt == null)
                .OrderByDescending(a => a.RequestedAt)
                .FirstOrDefaultAsync(ct);

            if (activeAnnotation is null || !activeAnnotation.FlaggedDocumentTypeIds.Contains(command.DocumentTypeId))
            {
                return new UploadDocumentResult.NotEditable(
                    "This document was not flagged in the reviewer's info request.");
            }
        }

        if (command.SizeBytes <= 0 || command.SizeBytes > FileTypeSniffer.MaxSizeBytes)
        {
            return new UploadDocumentResult.TooLarge();
        }

        var extension = Path.GetExtension(command.OriginalFileName).ToLowerInvariant();
        if (!FileTypeSniffer.AllowedExtensionToContentType.TryGetValue(extension, out var expectedContentType))
        {
            return new UploadDocumentResult.UnsupportedType();
        }

        await using var buffered = new MemoryStream();
        await command.Content.CopyToAsync(buffered, ct);
        buffered.Position = 0;

        var header = new byte[16];
        var headerRead = await buffered.ReadAsync(header.AsMemory(0, 16), ct);
        buffered.Position = 0;

        if (!FileTypeSniffer.TryDetectContentType(header[..Math.Max(headerRead, 0)], out var sniffedContentType) || sniffedContentType != expectedContentType)
        {
            await auditLogger.LogAsync("SupplierDocument", supplier.Id, "document_upload_content_mismatch", scope.UserId, referenceCode: supplier.ReferenceCode, ct: ct);
            return new UploadDocumentResult.ContentMismatch();
        }

        var existingVersions = await db.SupplierDocuments
            .Where(d => d.SupplierId == supplier.Id && d.DocumentTypeId == documentType.Id)
            .ToListAsync(ct);

        var nextVersion = existingVersions.Count == 0 ? 1 : existingVersions.Max(d => d.Version) + 1;
        foreach (var previous in existingVersions.Where(d => d.IsLatestVersion))
        {
            previous.SupersedeWithNewVersion();
        }

        var quarantineKey = $"quarantine/{supplier.Id}/{documentType.Id}/{Guid.NewGuid():N}{extension}";
        await fileStorage.SaveAsync(quarantineKey, buffered, expectedContentType, ct);

        var document = SupplierDocument.CreatePendingScan(
            supplier.Id, documentType.Id, nextVersion, quarantineKey,
            command.OriginalFileName, expectedContentType, command.SizeBytes, scope.UserId.Value,
            command.IssueDate, command.ExpiryDate);

        db.SupplierDocuments.Add(document);

        if (supplier.OnboardingState == SupplierOnboardingState.InfoRequested)
        {
            var activeAnnotation = await db.SupplierReviewAnnotations
                .Where(a => a.SupplierId == supplier.Id && a.ResolvedAt == null)
                .OrderByDescending(a => a.RequestedAt)
                .FirstAsync(ct);
            // Left unresolved until the supplier explicitly resubmits (STORY-03.3.1 AC2) - a
            // single re-upload doesn't automatically imply every flagged item is addressed.
            _ = activeAnnotation;
        }

        await auditLogger.LogAsync("SupplierDocument", document.Id, "document_uploaded", scope.UserId, referenceCode: supplier.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);

        backgroundJobs.Enqueue<DocumentScanJob>(job => job.ScanAsync(document.Id, CancellationToken.None));

        return new UploadDocumentResult.Success(ToDto(document));
    }

    internal static SupplierDocumentDto ToDto(SupplierDocument d) => new(
        d.Id, d.Version, d.State.ToString(), d.OriginalFileName, d.ContentType, d.SizeBytes,
        d.IssueDate, d.ExpiryDate, d.RejectReason, d.UploadedAt, d.ReviewedAt);
}
