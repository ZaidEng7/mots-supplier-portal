using MotsSupplierPortal.Domain.Proposals;

namespace MotsSupplierPortal.Domain.Rfqs;

/// <summary>A mandatory or optional qualifying condition/document the supplier must satisfy to
/// propose (DOMAIN-MODEL.md §5.4). DocumentTypeCode, when set, references reference.document_type
/// by code (same code-not-FK convention as RfqItem.CategoryCode).</summary>
public sealed class Requirement
{
    public Guid Id { get; init; }
    public Guid RfqId { get; init; }
    public string TextAr { get; set; } = null!;
    public string TextEn { get; set; } = null!;
    public bool IsMandatory { get; set; }
    public string? DocumentTypeCode { get; set; }

    /// <summary>
    /// A-2: which envelope a document answering this requirement belongs in.
    ///
    /// <para>The supplier tags each file at upload (OQ-009's two-envelope control, built since T-028
    /// with a Commercial default), and had nothing to tag against - so the RFQ now says what it expects.
    /// Null when this requirement asks for no document at all, which is most of them.</para>
    ///
    /// <para>Advisory, deliberately: it tells the supplier what the buyer expects, and it does NOT
    /// override the tag on the file. The knowledge of what a given file actually contains sits with
    /// whoever attached it, and a buyer's expectation silently re-tagging a supplier's document is how a
    /// price ends up in the technical envelope.</para>
    /// </summary>
    public ProposalDocumentEnvelope? ExpectedEnvelope { get; set; }
}
