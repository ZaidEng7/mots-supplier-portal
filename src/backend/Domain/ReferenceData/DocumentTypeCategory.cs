// Which categories a document type is required for.
//
// A link NARROWS. A required document type with no links here is required of every
// supplier; one with links is required only of suppliers working in a named category. A
// catering supplier can be asked for a food-safety certificate without every transport
// company being asked for one too.
//
// Reading an empty link set as "required for nothing" is the mistake this design has to
// avoid. It would empty the submit gate, the resubmit gate, the reviewer's approval gate
// and the completeness figure all at once, and a portal that lets an incomplete
// application through is worse than one that asks for too much.
//
// The rule was in the specification long before the schema could express it: a document
// type had no category field and no collection, so the condition was not merely unapplied,
// it was unrepresentable. This is the representation.
//
// Applying it to suppliers who were already approved under the old flat rule was a
// separate decision, taken deliberately, because it is a one-way door on a live registry.
//
// Categories are referenced by CODE rather than by id here, matching every other
// category reference in the schema. The code is what live rows carry, so it is the stable
// identifier.
//
// The derivation lives in RequiredDocumentTypeResolver, which all four gates ask rather
// than each repeating the rule.

namespace MotsSupplierPortal.Domain.ReferenceData;

public sealed class DocumentTypeCategory
{
    public Guid Id { get; init; }

    public Guid DocumentTypeId { get; init; }

    public required string CategoryCode { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
