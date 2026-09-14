// What an administrator may ask of reference data: add a row, edit one, and switch one on or off.
//
// The code cannot be changed by an edit. It is what every live row pointing at this item refers to, and
// nothing cascades, so renaming it would silently change what a historical award record says it was for.
//
// Deactivating is the only form of removal offered, for the same reason.
//
// Setting a document type's categories is its own command, because it applies to document types only and
// narrows which suppliers that type is required of.

namespace MotsSupplierPortal.Application.ReferenceData;

public sealed record CreateReferenceItemCommand(
    string Table, string Code, string NameAr, string NameEn, bool? IsRequired, bool? ExpiryTracked,
    bool? IsAwardCritical);

public sealed record UpdateReferenceItemCommand(
    string Table, string Code, string NameAr, string NameEn, bool? IsRequired, bool? ExpiryTracked,
    bool? IsAwardCritical);

public sealed record SetReferenceItemActiveCommand(string Table, string Code, bool IsActive);

public sealed record SetDocumentTypeCategoriesCommand(string DocumentTypeCode, IReadOnlyList<string> CategoryCodes);
