namespace MotsSupplierPortal.Domain.Proposals;

/// <summary>
/// Which of the two envelopes (OQ-009) a proposal's supporting file belongs to.
///
/// <para><b>Why this exists as a stored field rather than a convention.</b> Proposal pricing is
/// already structurally separated: <c>ProposalItem</c> is its own table and no buyer-side read
/// produces <c>ProposalItemDto</c> at all. Files had no equivalent separation, because a file's
/// contents are opaque to us - a supplier can put a priced bill of quantities inside something
/// captioned "technical compliance matrix" and nothing in the system can tell.</para>
///
/// <para><b>Commercial is the default, and that is the whole safety property.</b> An unlabelled
/// file, and every row that predates this field, is treated as pricing. Getting the default wrong
/// in this direction hides a technical document from an evaluator, which is visible and
/// complainable; getting it wrong in the other direction leaks a competitor's prices during
/// scoring, which is silent and unrecoverable. D-7 chose the recoverable failure.</para>
///
/// <para><b>D-7 is a decision, not a spec citation.</b> No FEAT or BRULE assigns envelopes to
/// proposal attachments. Today the buyer-side download gate refuses both kinds until the
/// evaluation reaches Consolidated, so the value is not yet what decides access - it is stored now
/// so that relaxing the gate for technical files later is a change to one predicate rather than a
/// migration plus a backfill nobody can perform after the fact.</para>
/// </summary>
public enum ProposalDocumentEnvelope
{
    /// <summary>The gated side, and the default. Anything that might carry pricing.</summary>
    Commercial,

    /// <summary>Explicitly declared by the uploading supplier as carrying no pricing.</summary>
    Technical,
}
