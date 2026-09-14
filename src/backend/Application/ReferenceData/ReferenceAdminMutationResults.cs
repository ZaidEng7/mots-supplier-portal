// What an administrator's write to reference data can answer.
//
// A code that already exists on that table is a refusal rather than a silent overwrite. An administrator
// who believes they are adding a new document type must not quietly replace an existing one.
//
// Unknown category codes are named in the refusal, because telling somebody that one of these is not a
// category sends them to compare two lists by eye.

namespace MotsSupplierPortal.Application.ReferenceData;

public abstract record ReferenceDataResult
{
    public sealed record Success(ReferenceItemDto Item) : ReferenceDataResult;
    public sealed record UnknownTable : ReferenceDataResult;
    public sealed record NotFound : ReferenceDataResult;
    public sealed record DuplicateCode : ReferenceDataResult;
    public sealed record Invalid(string Message) : ReferenceDataResult;
}

public abstract record SetDocumentTypeCategoriesResult
{
    public sealed record Success(DocumentTypeCategoryLinksDto Links) : SetDocumentTypeCategoriesResult;
    public sealed record UnknownDocumentType : SetDocumentTypeCategoriesResult;

    public sealed record UnknownCategories(IReadOnlyList<string> Codes) : SetDocumentTypeCategoriesResult;
}
