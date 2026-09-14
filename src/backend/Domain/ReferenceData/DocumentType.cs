// A kind of document a supplier may be asked for: a commercial registration, a tax
// certificate, a chamber membership.
//
// IsRequired means an application cannot be submitted without it. ExpiryTracked means the
// supplier must give an expiry date, and the portal warns as that date approaches.
//
// IsAwardCritical is the one that carries consequences: when a document of this type
// expires, the supplier is suspended automatically.
//
// It is false on every seeded type, deliberately. The ministry has not yet said which
// documents are award-critical, and the two ways of being wrong are not symmetric.
// Flagging a type the ministry would not have chosen suspends real suppliers, blocks their
// participation, and reactivating them later does not undo having been blocked. Flagging
// none leaves behaviour exactly as it is today. So the mechanism ships complete and
// dormant, and the ministry's answer becomes a data change rather than a deployment.
//
// Stated plainly: the auto-suspension rule does nothing in production until somebody sets
// this flag. That is intended, not an oversight. The flag is editable from the
// administration screens for that reason - it used to be settable only by a migration,
// which meant a ministry that HAD decided still could not record the decision.

namespace MotsSupplierPortal.Domain.ReferenceData;

public sealed class DocumentType
{
    public Guid Id { get; init; }
    public required string Code { get; init; }
    public required string NameAr { get; set; }
    public required string NameEn { get; set; }
    public bool IsRequired { get; set; }
    public bool ExpiryTracked { get; set; }
    public bool IsAwardCritical { get; set; }
    public bool IsActive { get; set; } = true;
}
