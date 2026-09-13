// One renewal reminder that has been sent for one document at one step of the reminder ladder.
//
// It is a table rather than a flag because the old de-duplication was an accident. Marking a document
// as expiring soon throws if it already is, so the expiry job could only ever notify once. That worked,
// but not for the reason it appeared to: it de-duplicated because the transition happened to be
// one-way, and the same accident capped the number of reminders at exactly one. The rule asks for an
// escalating ladder, which the accident cannot express at all. Recording what was sent makes
// de-duplication a fact rather than a consequence.
//
// The key is the document, its version and the threshold, deliberately not the job run. Keying on
// "have we run today" assumes the job runs once a day, and it will not: a retry, a host restart, a
// manual trigger or a schedule change all re-run it, and any of those would notify again. Keying on
// what was actually communicated is unaffected by how often the job executes.
//
// The version is what resets the ladder. A re-upload supersedes the old row and creates a new version,
// which has no reminder rows and starts the ladder from the top. That is correct, since a renewed
// document is not part-way through being chased, and it falls out of the key rather than needing
// anything to be deleted. The version is stored explicitly even though a new version also has a new
// identifier, because the rule is that reminders belong to a version, and a key that says so survives
// any later change to how versions are represented.
//
// ThresholdDays is the step this reminder was sent for, 30, 14 or 3 by default. It stores the
// configured number rather than a position in the list, so changing the ladder cannot silently
// re-interpret reminders already sent.
//
// WasSent says whether an email actually went out for this step, or whether the step was simply
// recorded as already passed. A document first seen with three days left has crossed all three steps at
// once; sending three emails would be absurd, and sending one a day for the next three days would be
// worse, so the passed steps are recorded without being sent. Keeping the distinction means the record
// describes what the supplier received rather than only what the job decided.

namespace MotsSupplierPortal.Domain.Suppliers;

public sealed class DocumentExpiryReminder
{
    public Guid Id { get; private init; }
    public Guid SupplierDocumentId { get; private init; }
    public int DocumentVersion { get; private init; }

    public int ThresholdDays { get; private init; }

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
