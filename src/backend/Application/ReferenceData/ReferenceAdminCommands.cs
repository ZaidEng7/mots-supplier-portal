namespace MotsSupplierPortal.Application.ReferenceData;

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

public sealed record SetDocumentTypeCategoriesCommand(string DocumentTypeCode, IReadOnlyList<string> CategoryCodes);
