namespace MotsSupplierPortal.Application.Suppliers;

public sealed record OfferingDto(
    Guid Id, string NameAr, string NameEn, string? Description,
    string CategoryCode, string UnitOfMeasureCode, decimal? PriceAmount, string? CurrencyCode, bool IsActive);

public sealed record CreateOfferingCommand(
    string NameAr, string NameEn, string? Description,
    string CategoryCode, string UnitOfMeasureCode, decimal? PriceAmount, string? CurrencyCode);

public sealed record UpdateOfferingCommand(
    Guid OfferingId, string NameAr, string NameEn, string? Description,
    string CategoryCode, string UnitOfMeasureCode, decimal? PriceAmount, string? CurrencyCode);

/// <summary>FEAT-06.1 AC1/AC4: category/UoM referential integrity is validated here rather than a
/// DB foreign key, matching CategoryLink's own established pattern (see its doc comment) - there
/// is no DB-level FK from CategoryLink.CategoryCode to Category.Code either.</summary>
public abstract record OfferingMutationResult
{
    public sealed record Success(OfferingDto Offering) : OfferingMutationResult;
    public sealed record NotFoundOrOutOfScope : OfferingMutationResult;
    public sealed record InvalidCategory : OfferingMutationResult;
    public sealed record InvalidUnitOfMeasure : OfferingMutationResult;
    public sealed record InvalidCurrency : OfferingMutationResult;
}

public interface IListOfferingsHandler
{
    Task<IReadOnlyList<OfferingDto>> HandleAsync(CancellationToken ct);
}

public interface ICreateOfferingHandler
{
    Task<OfferingMutationResult> HandleAsync(CreateOfferingCommand command, CancellationToken ct);
}

public interface IUpdateOfferingHandler
{
    Task<OfferingMutationResult> HandleAsync(UpdateOfferingCommand command, CancellationToken ct);
}

public interface IDeactivateOfferingHandler
{
    Task<OfferingMutationResult> HandleAsync(Guid offeringId, CancellationToken ct);
}
