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
    bool? IsRequired = null, bool? ExpiryTracked = null);

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
    string Table, string Code, string NameAr, string NameEn, bool? IsRequired, bool? ExpiryTracked);

/// <summary>
/// Editing an existing row. <b>The code cannot be changed</b> - see DECISIONS-TAKEN.md D-28: it is
/// the foreign key in every live row that points at this item, and there is no cascade, so renaming
/// it would silently change what a historical award record says it was for.
/// </summary>
public sealed record UpdateReferenceItemCommand(
    string Table, string Code, string NameAr, string NameEn, bool? IsRequired, bool? ExpiryTracked);

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
