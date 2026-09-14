// Building an attachment's address in the object store, from server-side values only.
//
//
// THE DEFECT THIS CLOSES
//
// Both upload paths built their key by interpolation, and BOTH interpolated values were caller input.
//
// The filename is the obvious one: a name containing a path traversal shapes the key.
//
// The reference code is the one that is easy to miss, because it looks like it belongs to us. It is a route
// parameter, and it is not validated against a real tender until the handler runs, several lines after the bytes
// have already been written under that key.
//
// So the key is a fixed prefix and an identifier this process generated, and nothing else. Flat rather than
// grouped by reference code: the grouping was the only thing the interpolation bought, and the row already records
// which parent the object belongs to.
//
// The filename is not lost. It is stored on the row and is what the download's own header reads. It is metadata
// about the object, never part of its address.
//
// Existing objects are unaffected, because a download reads the key FROM the row, so rows written under the old
// scheme keep resolving to their old keys. Nothing needs migrating; only new uploads take the new shape.
//
//
// NEITHER OF THESE PATHS IS QUARANTINED, AND SUPPLIER DOCUMENTS ARE
//
// Stated here because both callers share it. That asymmetry is deliberate rather than unnoticed.
//
// It is not blocked on the scanner, which is a real client, registered and fail-closed. It is blocked because
// quarantine-first is a STATE MACHINE, and neither a tender attachment nor a bid document has a state field at
// all, so there is nothing to gate a download on and nothing to say what the rows already in the table are.
//
// That last question is an open one tagged as needing business confirmation. Guessing it means either quarantining
// files procurement expects to publish at once, or adding a scan-state column that always reads clean, which is
// worse than no column because it looks like a control.

namespace MotsSupplierPortal.Infrastructure.Storage;

public static class AttachmentStorageKey
{
    public const string RfqAttachmentPrefix = "rfq-attachments";
    public const string ProposalDocumentPrefix = "proposal-documents";

    public static string For(string prefix) => $"{prefix}/{Guid.CreateVersion7()}";
}
