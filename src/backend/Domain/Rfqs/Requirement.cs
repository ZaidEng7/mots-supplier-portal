// A condition a supplier must satisfy to bid, and optionally the kind of document that proves it.
//
// IsMandatory decides whether an answer is required before the bid can be submitted.
//
// DocumentTypeCode, when set, points at a document type by code rather than by a database link, the
// same convention the item's category code follows.
//
// ExpectedEnvelope says which envelope the buyer expects a document answering this requirement to
// belong in. It is null when the requirement asks for no document at all, which is most of them.
//
// Suppliers already tag each file as they upload it, and had nothing to tag it against, so the tender
// now says what it expects.
//
// It is advisory on purpose, and it does not override the tag on the file. Whoever attached a file
// knows what is actually inside it, and a buyer's expectation silently re-tagging a supplier's
// document is exactly how a price ends up in the technical envelope.

namespace MotsSupplierPortal.Domain.Rfqs;

using MotsSupplierPortal.Domain.Proposals;

public sealed class Requirement
{
    public Guid Id { get; init; }
    public Guid RfqId { get; init; }
    public string TextAr { get; set; } = null!;
    public string TextEn { get; set; } = null!;
    public bool IsMandatory { get; set; }
    public string? DocumentTypeCode { get; set; }

    public ProposalDocumentEnvelope? ExpectedEnvelope { get; set; }
}
