namespace MotsSupplierPortal.Domain.ReferenceData;

/// <summary>
/// BRULE-016's missing shape: which categories a document type is required for.
///
/// <para><b>The rule says required documents are conditioned on the supplier's categories, and the schema
/// could not express that at all.</b> `DocumentType` has no category field and no collection - the condition
/// was not merely unapplied, it was unrepresentable. This is the representation.</para>
///
/// <para><b>Nothing reads it yet, and that is deliberate.</b> The four sites that derive the required set -
/// DocumentCompletenessEvaluator (the submit gate, the resubmit gate and the reviewer's approval gate),
/// GetSupplierHandler, SupplierDashboardHandler and ListSupplierDocumentsHandler - still use the flat
/// `IsRequired && IsActive` filter. Switching them over changes what "complete" means for every supplier in
/// the system, and two things have to be decided first:</para>
///
/// <para>1. <b>The data.</b> Which document types attach to which categories is a ministry decision, and an
/// empty link table read as "required for nothing" would silently drop every required document from every
/// gate - a portal that lets an incomplete application through is worse than one that asks for too much.</para>
///
/// <para>2. <b>Retroactivity.</b> Suppliers already approved under the flat rule were approved against a list
/// that may not be theirs under a category-conditioned one. Whether the tightening reaches back is a decision
/// about live suppliers, not a query change - see COMPLETION-INVENTORY.md §4.2 and the question logged there.</para>
///
/// <para>So: the shape and its admin surface exist, the links can be recorded, and the derivation stays flat
/// until somebody with standing answers both questions.</para>
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
