// Which of the two envelopes a bid's supporting file belongs to: the priced one or the technical one.
//
// This is a stored answer rather than a convention because a file's contents are opaque to us.
// Pricing is already separated structurally, since a bid's priced lines are their own table that no
// buyer-side read returns at all. Files had no equivalent separation: a supplier can put a priced
// bill of quantities inside something captioned "technical compliance matrix", and nothing in the
// system can tell.
//
// Commercial is the default, and that is the whole safety property. An unlabelled file, and every row
// that predates this field, is treated as pricing. Getting the default wrong in this direction hides
// a technical document from an evaluator, which is visible and complainable. Getting it wrong in the
// other direction leaks a competitor's prices during scoring, which is silent and cannot be undone.
// The recoverable failure was chosen deliberately.
//
// Nothing in the written requirements assigns envelopes to a bid's attachments; this is a decision
// taken here. Today the buyer-side download refuses both kinds until evaluation has been
// consolidated, so the value is not yet what decides access. It is stored now so that relaxing the
// rule for technical files later is a change to one condition rather than a migration plus a
// back-fill that nobody could perform after the fact.

namespace MotsSupplierPortal.Domain.Proposals;

public enum ProposalDocumentEnvelope
{
    Commercial,

    Technical,
}
