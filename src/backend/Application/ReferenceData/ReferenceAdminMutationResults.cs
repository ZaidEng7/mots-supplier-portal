namespace MotsSupplierPortal.Application.ReferenceData;

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

public abstract record SetDocumentTypeCategoriesResult
{
    public sealed record Success(DocumentTypeCategoryLinksDto Links) : SetDocumentTypeCategoriesResult;
    public sealed record UnknownDocumentType : SetDocumentTypeCategoriesResult;

    /// <param name="Codes">Named, because "one of these is not a category" sends an administrator to compare
    /// two lists by eye.</param>
    public sealed record UnknownCategories(IReadOnlyList<string> Codes) : SetDocumentTypeCategoriesResult;
}
