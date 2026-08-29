using System.Globalization;
namespace MotsSupplierPortal.Domain.Suppliers;

/// <summary>
/// A single uploaded version of a compliance document (FR-DOC-002/006/007). Versioning is
/// append-only: a re-upload creates a new row and flips <see cref="IsLatestVersion"/> on the old
/// one rather than mutating history (FEAT-05.6).
/// </summary>
public sealed class SupplierDocument
{
    public Guid Id { get; private init; }
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

    /// <summary>
    /// BRULE-020, both halves.
    ///
    /// A type that tracks expiry must be given a valid FUTURE expiry date at upload. Previously
    /// `ExpiryTracked = true` silently accepted null and past dates, so a document could be filed
    /// as current while already expired, or with no expiry at all - and the expiry job, which
    /// filters on `ExpiryDate != null`, would never look at it again.
    ///
    /// The second half - "types without expiry never enter ExpiringSoon/Expired" - is enforced
    /// structurally rather than by convention: a non-tracked type has its expiry date DISCARDED
    /// here, so no such row can carry a date for the job to act on. Relying on callers not to send
    /// one would be correctness by coincidence.
    /// </summary>
    public static SupplierDocument CreatePendingScan(
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
                // InvariantCulture, and this is not defensive tidiness - it is a crash that was
                // reproduced. Interpolating a DateOnly uses CurrentCulture, and on an Arabic-locale
                // host that is the Umm al-Qura calendar, which only supports 1900-2077 Gregorian.
                // Formatting anything outside that range throws ArgumentOutOfRangeException from
                // INSIDE this exception's construction, so the guard that should have returned a
                // clean 400 produced an unhandled 500 instead.
                //
                // Same family as MSP-60, which fixed the PARSING side at DocumentEndpoints.cs.
                // This is the formatting side, and it was introduced by this very validation.
                throw new DomainException(
                    $"The expiry date {expiryDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} is not in the future; a document cannot be filed as current while already expired.");
            }
        }
        else
        {
            // Discarded, not merely ignored - see the summary above.
            expiryDate = null;
        }

        return new SupplierDocument
        {
            Id = Guid.CreateVersion7(),
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

    /// <summary>Scan came back clean: object was moved to the clean prefix, the document becomes
    /// visible/downloadable and counts toward completeness (docs/security §4.1).</summary>
    public void MarkScanClean(string cleanKey)
    {
        if (State != DocumentState.PendingScan)
        {
            throw new DomainException($"Cannot mark scan clean from state '{State}'; only 'PendingScan' is valid.");
        }

        StorageKey = cleanKey;
        State = DocumentState.Uploaded;
    }

    /// <summary>Scan found malware: the object itself is deleted by the caller; this row is kept
    /// (ScanRejected) purely as an audit trail so the supplier sees why re-upload is required.</summary>
    public void MarkScanRejected()
    {
        if (State != DocumentState.PendingScan)
        {
            throw new DomainException($"Cannot reject scan from state '{State}'; only 'PendingScan' is valid.");
        }

        State = DocumentState.ScanRejected;
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

    /// <summary>Whether this version currently satisfies its DocumentType requirement for the
    /// onboarding-submit completeness gate (docs/architecture/DOMAIN-MODEL.md §5.3 invariant).</summary>
    public bool SatisfiesSubmitRequirement =>
        State is DocumentState.Uploaded or DocumentState.UnderReview or DocumentState.Approved or DocumentState.ExpiringSoon;

    // BlocksApplicationApproval was removed by MSP-91 rather than left unused.
    //
    // It encoded "approval is blocked only by Rejected/ScanRejected/Expired", which read as the
    // whole of the 2026-08-26 product-owner decision and was in fact narrower than it. That decision
    // said approval must not require every document to be individually APPROVED. It said nothing
    // about missing or unscanned documents, and this property let both through.
    //
    // The approval gate now uses SatisfiesSubmitRequirement - the same predicate as the submit gate,
    // one vocabulary instead of two. Deleting the old property rather than leaving it unreferenced
    // is deliberate: a property whose name states the superseded rule is exactly what the next
    // person reaches for, and this codebase has a register full of correct-looking things that were
    // describing a rule nobody held any more.

    /// <summary>
    /// BRULE-018: a Rejected or Expired document flags the profile incomplete until replaced with
    /// an approved version.
    ///
    /// Deliberately narrower than <see cref="SatisfiesSubmitRequirement"/>, which the approval gate
    /// uses. A PendingScan or ScanRejected document blocks approval - the file never became a
    /// document - but must NOT flag an already-approved supplier's profile incomplete, because there
    /// is nothing for them to replace yet. The two predicates look similar and answer different
    /// questions; collapsing them would let a scan failure silently change an approved supplier's
    /// status.
    /// </summary>
    public bool FlagsProfileIncomplete =>
        State is DocumentState.Rejected or DocumentState.Expired;
}
