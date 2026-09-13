// One uploaded version of a compliance document.
//
// Versioning only ever adds: re-uploading creates a new row and marks the old one as no longer the
// latest, rather than changing history.
//
// ReferenceCode is the public identifier, DOC-2026-000001. Internal identifiers are never exposed in
// web addresses, payloads or errors, and public references are short human-readable codes. That covers
// payloads, not only addresses: an earlier comment elsewhere justified emitting the internal identifier
// in a response body on the grounds that the rule governed paths only, and that reading was wrong.
// Neither the prefix nor the shape is invented; both are transcribed from the written contract, and the
// code comes from the same counter every other reference code uses, keyed by prefix, so a new prefix
// needs no database change.
//
//
// UPLOAD
//
// A document type that tracks expiry must be given an expiry date in the future. That used to be
// accepted as blank or in the past, so a document could be filed as current while already expired, or
// with no expiry at all, and the expiry job, which looks only at documents that have a date, would never
// look at it again.
//
// The other half of the rule, that types without expiry never become expiring or expired, is enforced by
// structure rather than by asking callers nicely: a type that does not track expiry has its date
// discarded here, so no such row can carry a date for the job to act on.
//
// The expiry message formats the date with the invariant calendar, and that is not defensive tidiness
// but a crash that was reproduced. Interpolating a date uses the host's own culture, and on an
// Arabic-locale host that is the Umm al-Qura calendar, which covers only 1900 to 2077. Formatting
// anything outside that range throws from inside this exception's own construction, so the guard that
// should have returned a clean refusal produced a server error instead. The parsing side of the same
// problem was fixed earlier at the endpoint; this is the formatting side, and this very validation
// introduced it.
//
//
// THE PIPELINE
//
// MarkScanClean is the scanner coming back clean: the file moves to the clean area and the document
// becomes visible, downloadable and counted towards completeness.
//
// MarkScanRejected is the scanner finding malware. The file itself is deleted by the caller, and this
// row is kept purely as a record so the supplier can see why they have to upload again.
//
// EnterReview is the step the pipeline was missing. The under-review state was read by three guards and
// assigned by nothing, so the rule that a replaced document returns to review stopped halfway, and the
// reviewer's own query for documents under review or rejected matched nothing that had ever existed.
//
// It is kept as its own transition rather than folded into the clean-scan step, even though the scan job
// calls them one after the other. Uploaded is what the row holds if the job dies between the two, and
// both approve and reject accept that state, so a crash there leaves a reviewable document rather than a
// dead end. Collapsing the two states would have traded this gap for its mirror image: an uploaded state
// nothing could reach.
//
// Approve and Reject both accept uploaded or under review, and a rejection needs a reason.
//
// MarkExpiringSoon and MarkExpired are driven by the passage of time rather than by a person.
//
//
// THE TWO COMPLETENESS QUESTIONS
//
// SatisfiesSubmitRequirement answers whether this version currently satisfies its document type for the
// purpose of letting a supplier submit their application, and the application-approval gate uses the
// same answer.
//
// There used to be a second property for the approval gate, and it was deleted rather than left unused.
// It encoded that approval is blocked only by a rejection, a failed scan or an expiry, which read like
// the whole of the product owner's decision and was in fact narrower than it. That decision said
// approval must not require every document to be individually approved. It said nothing about missing or
// unscanned documents, and that property let both through. Deleting it rather than leaving it
// unreferenced is deliberate: a property whose name states a superseded rule is exactly what the next
// person reaches for.
//
// FlagsProfileIncomplete answers a different question: whether this document currently makes an
// already-approved supplier's profile incomplete until it is replaced.
//
// It is deliberately narrower than the other one. A document still awaiting a scan, or one the scanner
// refused, blocks approval, because the file never became a document. But it must not flag an
// already-approved supplier's profile as incomplete, because there is nothing for them to replace yet.
// The two look similar and answer different questions, and collapsing them would let a scan failure
// silently change an approved supplier's standing.

namespace MotsSupplierPortal.Domain.Suppliers;

using System.Globalization;

public sealed class SupplierDocument
{
    public Guid Id { get; private init; }

    public string ReferenceCode { get; private set; } = null!;
    public Guid SupplierId { get; private init; }
    public Guid DocumentTypeId { get; private init; }
    public int Version { get; private init; }
    public bool IsLatestVersion { get; private set; }
    public DocumentState State { get; private set; }
    public string StorageKey { get; private set; } = null!;
    public required string OriginalFileName { get; init; }
    public required string ContentType { get; init; }
    public long SizeBytes { get; init; }
    public DateOnly? IssueDate { get; init; }
    public DateOnly? ExpiryDate { get; init; }
    public string? RejectReason { get; private set; }
    public Guid UploadedByUserId { get; private init; }
    public DateTimeOffset UploadedAt { get; private init; }
    public Guid? ReviewedByUserId { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }

    private SupplierDocument() { }

    public static SupplierDocument CreatePendingScan(
        string referenceCode,
        Guid supplierId, Guid documentTypeId, int version, string quarantineKey,
        string originalFileName, string contentType, long sizeBytes, Guid uploadedByUserId,
        DateOnly? issueDate, DateOnly? expiryDate, bool expiryTracked, DateOnly today)
    {
        if (expiryTracked)
        {
            if (expiryDate is null)
            {
                throw new DomainException("This document type requires an expiry date.");
            }

            if (expiryDate <= today)
            {
                throw new DomainException(
                    $"The expiry date {expiryDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} is not in the future; a document cannot be filed as current while already expired.");
            }
        }
        else
        {
            expiryDate = null;
        }

        return new SupplierDocument
        {
            Id = Guid.CreateVersion7(),
            ReferenceCode = referenceCode,
            SupplierId = supplierId,
            DocumentTypeId = documentTypeId,
            Version = version,
            IsLatestVersion = true,
            State = DocumentState.PendingScan,
            StorageKey = quarantineKey,
            OriginalFileName = originalFileName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            IssueDate = issueDate,
            ExpiryDate = expiryDate,
            UploadedByUserId = uploadedByUserId,
            UploadedAt = DateTimeOffset.UtcNow,
        };
    }

    public void SupersedeWithNewVersion() => IsLatestVersion = false;

    public void MarkScanClean(string cleanKey)
    {
        if (State != DocumentState.PendingScan)
        {
            throw new DomainException($"Cannot mark scan clean from state '{State}'; only 'PendingScan' is valid.");
        }

        StorageKey = cleanKey;
        State = DocumentState.Uploaded;
    }

    public void MarkScanRejected()
    {
        if (State != DocumentState.PendingScan)
        {
            throw new DomainException($"Cannot reject scan from state '{State}'; only 'PendingScan' is valid.");
        }

        State = DocumentState.ScanRejected;
    }

    public void EnterReview()
    {
        if (State != DocumentState.Uploaded)
        {
            throw new DomainException($"Cannot enter review from state '{State}'; only 'Uploaded' is valid.");
        }

        State = DocumentState.UnderReview;
    }

    public void Approve(Guid reviewerUserId)
    {
        if (State is not (DocumentState.Uploaded or DocumentState.UnderReview))
        {
            throw new DomainException($"Cannot approve from state '{State}'; only 'Uploaded' or 'UnderReview' is valid.");
        }

        State = DocumentState.Approved;
        ReviewedByUserId = reviewerUserId;
        ReviewedAt = DateTimeOffset.UtcNow;
    }

    public void Reject(Guid reviewerUserId, string reason)
    {
        if (State is not (DocumentState.Uploaded or DocumentState.UnderReview))
        {
            throw new DomainException($"Cannot reject from state '{State}'; only 'Uploaded' or 'UnderReview' is valid.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A rejection reason is required.");
        }

        State = DocumentState.Rejected;
        RejectReason = reason;
        ReviewedByUserId = reviewerUserId;
        ReviewedAt = DateTimeOffset.UtcNow;
    }

    public void MarkExpiringSoon()
    {
        if (State != DocumentState.Approved)
        {
            throw new DomainException($"Cannot mark expiring-soon from state '{State}'; only 'Approved' is valid.");
        }

        State = DocumentState.ExpiringSoon;
    }

    public void MarkExpired()
    {
        if (State is not (DocumentState.Approved or DocumentState.ExpiringSoon))
        {
            throw new DomainException($"Cannot mark expired from state '{State}'; only 'Approved' or 'ExpiringSoon' is valid.");
        }

        State = DocumentState.Expired;
    }

    public bool SatisfiesSubmitRequirement =>
        State is DocumentState.Uploaded or DocumentState.UnderReview or DocumentState.Approved or DocumentState.ExpiringSoon;

    public bool FlagsProfileIncomplete =>
        State is DocumentState.Rejected or DocumentState.Expired;
}
