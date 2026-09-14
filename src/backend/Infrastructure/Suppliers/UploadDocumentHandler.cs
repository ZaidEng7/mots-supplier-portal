// A supplier uploads a document, and it waits for the virus scanner before it counts for anything.
//
// The file's type, size and leading bytes are checked, it is written to the quarantine area, and the scan
// is queued. Until the scan comes back clean the document is neither downloadable nor able to satisfy a
// requirement.
//
//
// AN APPROVED SUPPLIER MAY UPLOAD, AND THAT IS A RENEWAL RATHER THAN A LOOPHOLE
//
// Documents expire. The system tracks the date, warns that one is expiring soon, moves it to expired on a
// daily job, and suspends the supplier when an award-critical one goes.
//
// Until this was permitted, the supplier's own documents screen offered them an expiry field and a file
// picker and then refused the upload. A reviewer could not help either, because requesting information
// needs the application to be under review. A supplier suspended for an expired certificate had no way,
// through any screen, to replace it. The only remedy was editing the database.
//
// The renewal rides the DOCUMENT lifecycle, which already exists for exactly this: the new version is
// uploaded, scanned, and waits for a reviewer beside the version it replaces. The supplier's own onboarding
// state is untouched, because a company whose tax certificate is a year newer is not a company that needs
// onboarding again.
//
// While an information request is open the upload is narrowed to the document types the reviewer actually
// flagged, and the request is left open until the supplier explicitly resubmits. One re-upload does not
// imply every flagged item is addressed.
//
//
// THE FILE IS NEVER HELD WHOLE IN MEMORY
//
// The stream handed in is the framework's own buffering stream: small requests stay in memory, anything
// past the configured threshold spools to a bounded temporary file, and either way it can be rewound.
//
// So the type sniff reads sixteen header bytes from that stream and rewinds it, rather than copying the
// whole upload into a second fully-materialised buffer first. The copy was the actual defect; reading a few
// header bytes never required it. A stream that cannot rewind is a programming error here rather than a
// bad request, which is why it throws.
//
// The declared extension and the sniffed bytes must agree. A mismatch is audited, because a file claiming
// to be a certificate and carrying something else is worth a record.
//
//
// THE PUBLIC CODE IS ALLOCATED BEFORE THE DOCUMENT IS CONSTRUCTED
//
// By the same atomic counter every other public code uses. A gap when the transaction rolls back is the
// documented trade: the database's own sequences do not roll back either, and gaps are harmless where reuse
// is not.
//
// If construction then refuses the dates, the file has already been written to quarantine. It is left
// there rather than deleted, because the scanning pipeline and the retention job own that area, and a
// half-deleted upload is harder to reason about than an orphaned one.
//
//
// THE SCAN STATUS ON THE READ MODEL IS DERIVED
//
// Read off the state machine that already knows: pending while the row is still in quarantine, rejected
// once the scanner has objected, and clean for every state a document can only reach by passing the scan.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Registrations;
using MotsSupplierPortal.Infrastructure.Storage;

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

        if (supplier.OnboardingState is SupplierOnboardingState.Draft or SupplierOnboardingState.Submitted
            or SupplierOnboardingState.UnderReview or SupplierOnboardingState.Rejected)
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

        if (!command.Content.CanSeek)
        {
            throw new InvalidOperationException(
                "UploadDocumentCommand.Content must be seekable - the upload pipeline sniffs header bytes then rewinds before streaming to storage.");
        }

        var header = new byte[16];
        var headerRead = await command.Content.ReadAsync(header.AsMemory(0, 16), ct);
        command.Content.Position = 0;

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
        await fileStorage.SaveAsync(quarantineKey, command.Content, expectedContentType, ct);

        SupplierDocument document;
        try
        {
            var referenceCode = await ReferenceCodeGenerator.NextCodeAsync(db, "DOC", ct);

            document = SupplierDocument.CreatePendingScan(
                referenceCode,
                supplier.Id, documentType.Id, nextVersion, quarantineKey,
                command.OriginalFileName, expectedContentType, command.SizeBytes, scope.UserId.Value,
                command.IssueDate, command.ExpiryDate,
                documentType.ExpiryTracked, DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime));
        }
        catch (DomainException ex)
        {
            return new UploadDocumentResult.InvalidExpiry(ex.Message);
        }

        db.SupplierDocuments.Add(document);

        if (supplier.OnboardingState == SupplierOnboardingState.InfoRequested)
        {
            var activeAnnotation = await db.SupplierReviewAnnotations
                .Where(a => a.SupplierId == supplier.Id && a.ResolvedAt == null)
                .OrderByDescending(a => a.RequestedAt)
                .FirstAsync(ct);
            _ = activeAnnotation;
        }

        await auditLogger.LogAsync("SupplierDocument", document.Id, "document_uploaded", scope.UserId, referenceCode: document.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);

        backgroundJobs.Enqueue<DocumentScanJob>(job => job.ScanAsync(document.Id, CancellationToken.None));

        return new UploadDocumentResult.Success(ToDto(document));
    }

    internal static SupplierDocumentDto ToDto(SupplierDocument d) => new(
        d.ReferenceCode, d.Version, d.State.ToString(), d.OriginalFileName, d.ContentType, d.SizeBytes,
        d.IssueDate, d.ExpiryDate, d.RejectReason, d.UploadedAt, d.ReviewedAt, ScanStatusOf(d.State));

    private static string ScanStatusOf(DocumentState state) => state switch
    {
        DocumentState.PendingScan => "Pending",
        DocumentState.ScanRejected => "Rejected",
        _ => "Clean",
    };
}
