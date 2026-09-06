namespace MotsSupplierPortal.Application.ReferenceData;

/// <summary>
/// T-034/T-059/FR-ADM-004: the admin write surface for reference data. Five tables - Category,
/// DocumentType, Currency, UnitOfMeasure, Region - were seed-only, so a ministry could not add a
/// document type without a deploy.
///
/// <para><b>One shape for all five, not five near-identical surfaces.</b> They differ only in which
/// extra flags they carry, and DocumentType is the only one with any. A per-table contract would be
/// five copies of the same four operations, and the fifth copy is where the audit call gets
/// forgotten.</para>
///
/// <para><b>FR-ADM-004 names SIX tables and only five exist.</b> Incoterm has no entity at all -
/// <c>Proposal.IncotermCode</c> is a free string validated by nothing. Recorded as its own backlog
/// row rather than invented here, because a code list nobody has supplied is not reference data.</para>
/// </summary>
public sealed record ReferenceItemDto(
    string Code, string NameAr, string NameEn, bool IsActive,
    // DocumentType only. Null on every other table rather than false, because "this table has no
    // such flag" and "this row has the flag off" are different facts.
    bool? IsRequired = null, bool? ExpiryTracked = null,
    /// <summary>
    /// BRULE-023's flag, on the wire for the first time. Expiry of an award-critical document suspends the
    /// supplier (DocumentExpiryJob.AutoSuspendForAwardCriticalExpiryAsync), and the rule has never been able
    /// to fire: the column exists, the job reads it, and no seeded type sets it - nor could anyone set it,
    /// because it was absent from this contract and from the admin screen. A migration was the only way in.
    ///
    /// <para><b>The values are NOT changed here.</b> Which document types are award-critical is a ministry
    /// decision about procurement risk, and "was suspended for a fortnight" is not undone by reactivation.
    /// This closes the code half so a ministry that has decided can record it; the decision itself stays
    /// open. See COMPLETION-INVENTORY.md §4.1.</para>
    /// </summary>
    bool? IsAwardCritical = null);

/// <summary>The tables an administrator may edit, named on the wire so a typo is a refusal rather
/// than a silent no-op against the wrong table.</summary>
public static class ReferenceTables
{
    public const string Categories = "categories";
    public const string DocumentTypes = "document-types";
    public const string Currencies = "currencies";
    public const string UnitsOfMeasure = "units-of-measure";
    public const string Regions = "regions";

    public static readonly string[] All =
        [Categories, DocumentTypes, Currencies, UnitsOfMeasure, Regions];
}

public sealed record CreateReferenceItemCommand(
    string Table, string Code, string NameAr, string NameEn, bool? IsRequired, bool? ExpiryTracked,
    bool? IsAwardCritical);

/// <summary>
/// Editing an existing row. <b>The code cannot be changed</b> - see DECISIONS-TAKEN.md D-28: it is
/// the foreign key in every live row that points at this item, and there is no cascade, so renaming
/// it would silently change what a historical award record says it was for.
/// </summary>
public sealed record UpdateReferenceItemCommand(
    string Table, string Code, string NameAr, string NameEn, bool? IsRequired, bool? ExpiryTracked,
    bool? IsAwardCritical);

/// <summary>Deactivation, which is the only form of removal offered - see D-28.</summary>
public sealed record SetReferenceItemActiveCommand(string Table, string Code, bool IsActive);

public abstract record ReferenceDataResult
{
    public sealed record Success(ReferenceItemDto Item) : ReferenceDataResult;
    public sealed record UnknownTable : ReferenceDataResult;
    public sealed record NotFound : ReferenceDataResult;
    /// <summary>A code that already exists on this table. A refusal rather than a silent upsert: an
    /// admin who thinks they are adding a type must not quietly overwrite one.</summary>
    public sealed record DuplicateCode : ReferenceDataResult;
    public sealed record Invalid(string Message) : ReferenceDataResult;
}

public interface IReferenceDataAdminHandler
{
    Task<IReadOnlyList<ReferenceItemDto>?> ListAsync(string table, bool includeInactive, CancellationToken ct);
    Task<ReferenceDataResult> CreateAsync(CreateReferenceItemCommand command, CancellationToken ct);
    Task<ReferenceDataResult> UpdateAsync(UpdateReferenceItemCommand command, CancellationToken ct);
    Task<ReferenceDataResult> SetActiveAsync(SetReferenceItemActiveCommand command, CancellationToken ct);
}

/// <summary>
/// BRULE-016. Which categories a document type is required for.
/// </summary>
/// <param name="CategoryCodes">Empty means no links recorded — which today means the same as every other
/// document type, because nothing derives the required set from these yet. It does NOT mean "required for
/// nothing"; see DocumentTypeCategory for why that distinction has to be settled before the derivation is
/// switched on.</param>
public sealed record DocumentTypeCategoryLinksDto(string DocumentTypeCode, IReadOnlyList<string> CategoryCodes);

public sealed record SetDocumentTypeCategoriesCommand(string DocumentTypeCode, IReadOnlyList<string> CategoryCodes);

public abstract record SetDocumentTypeCategoriesResult
{
    public sealed record Success(DocumentTypeCategoryLinksDto Links) : SetDocumentTypeCategoriesResult;
    public sealed record UnknownDocumentType : SetDocumentTypeCategoriesResult;

    /// <param name="Codes">Named, because "one of these is not a category" sends an administrator to compare
    /// two lists by eye.</param>
    public sealed record UnknownCategories(IReadOnlyList<string> Codes) : SetDocumentTypeCategoriesResult;
}

public interface IGetDocumentTypeCategoriesHandler
{
    Task<IReadOnlyList<DocumentTypeCategoryLinksDto>> HandleAsync(CancellationToken ct);
}

public interface ISetDocumentTypeCategoriesHandler
{
    Task<SetDocumentTypeCategoriesResult> HandleAsync(SetDocumentTypeCategoriesCommand command, CancellationToken ct);
}
