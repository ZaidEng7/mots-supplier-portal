namespace MotsSupplierPortal.Domain.Suppliers;

/// <summary>
/// One renewal reminder that has been sent for one document at one cadence step (BRULE-025,
/// FR-NOT-006).
///
/// <para><b>Why a table and not a flag.</b> De-duplication used to be a side effect of the state
/// machine: <c>MarkExpiringSoon</c> throws if the document is already ExpiringSoon, so the expiry
/// job could only ever notify once. That worked, but not for the reason it appeared to - it
/// de-duplicated because the transition happened to be one-way, and the same accident capped the
/// reminder count at exactly one. BRULE-025 asks for an escalating cadence, which the accident
/// cannot express at all. Recording what was sent makes de-duplication a fact rather than a
/// consequence.</para>
///
/// <para><b>The key is document + version + threshold, deliberately not the job run.</b> Keying on
/// "have we run today" assumes the job runs once a day. It will not: a retry, a host restart, a
/// manual trigger, or a schedule change all re-run it, and any of those would re-notify. Keying on
/// what was actually communicated is invariant to how often the job executes.</para>
///
/// <para><b>Version is what resets the cadence.</b> A re-upload supersedes the old row and creates a
/// new document version, so it has no reminder rows and starts the cadence from the top. That is
/// the correct behaviour - a renewed document is not part-way through being chased - and it falls
/// out of the key rather than needing a deletion step. Version is stored explicitly even though the
/// new version also carries a new Id: the rule is "reminders belong to a version", and a key that
/// states it survives any later change to how versioning is represented.</para>
/// </summary>
public sealed class DocumentExpiryReminder
{
    public Guid Id { get; private init; }
    public Guid SupplierDocumentId { get; private init; }
    public int DocumentVersion { get; private init; }

    /// <summary>The cadence step this reminder was sent for - 30, 14 or 3 by default. Stored as the
    /// configured number rather than an ordinal so a change to the cadence cannot silently
    /// re-interpret reminders already sent.</summary>
    public int ThresholdDays { get; private init; }

    /// <summary>Whether an email was actually dispatched for this step, or whether it was recorded
    /// as already-passed. A document first seen with three days left has crossed all three steps at
    /// once; sending three emails would be absurd and sending one a day for the next three days
    /// would be worse, so the passed steps are recorded without being sent. Keeping the distinction
    /// means the ledger describes what the supplier received, not just what the job decided.</summary>
    public bool WasSent { get; private init; }

    public DateTimeOffset RecordedAt { get; private init; }

    private DocumentExpiryReminder() { }

    public static DocumentExpiryReminder Record(
        Guid supplierDocumentId, int documentVersion, int thresholdDays, bool wasSent, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            SupplierDocumentId = supplierDocumentId,
            DocumentVersion = documentVersion,
            ThresholdDays = thresholdDays,
            WasSent = wasSent,
            RecordedAt = now,
        };
}
