namespace MotsSupplierPortal.Domain.ReferenceData;

/// <summary>
/// BRULE-016: which categories a document type is required for.
///
/// <para><b>The rule says required documents are conditioned on the supplier's categories, and the schema
/// could not express that at all.</b> `DocumentType` has no category field and no collection - the condition
/// was not merely unapplied, it was unrepresentable. This is the representation.</para>
///
/// <para><b>Read since D-59.</b> This shipped in batch 11 recorded-but-unread, because two questions came
/// before any gate could derive the required set from it: which types attach to which categories, and
/// whether a category-conditioned set reaches back to suppliers already approved under the flat one. D-59
/// answers both - on, and retroactive - and records why that is a one-way door on a live registry.</para>
///
/// <para><b>What a row means.</b> A link NARROWS: a required type with no links is required of every
/// supplier, and one with links is required only of suppliers holding a named category. Reading an empty
/// link set as "required for nothing" is the failure that reading had to avoid - it would have emptied the
/// submit gate, the resubmit gate, the reviewer's approval gate and the completeness figure at once, and a
/// portal that lets an incomplete application through is worse than one that asks for too much.</para>
///
/// <para>The derivation itself is <c>RequiredDocumentTypeResolver</c>, asked by all four sites rather than
/// repeated in each.</para>
/// </summary>
public sealed class DocumentTypeCategory
{
    public Guid Id { get; init; }

    public Guid DocumentTypeId { get; init; }

    /// <summary>A <c>Category.Code</c>. By CODE rather than id, matching how every other reference link in
    /// this schema points at a category (Offering.CategoryCode, SupplierCategoryLink) - and D-28's reason
    /// applies here too: the code is what live rows carry, so it is the stable identifier.</summary>
    public required string CategoryCode { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
