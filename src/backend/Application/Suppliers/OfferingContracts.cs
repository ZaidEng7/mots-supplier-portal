// The vocabulary for a supplier's catalogue: what an entry looks like, and what can be asked of it.
//
// The version is carried on the shape so a read can issue it as a precondition. The filter that does that
// looks for the property by name and does nothing without it.
//
//
// WHY THERE IS A SINGLE-ENTRY READ
//
// So the write precondition is obtainable. Every guarded write needs a read that issues the version it
// demands, and there was only a list.
//
// Requiring the precondition on deactivation without this read made the header unobtainable, so the guard
// refused every caller. That is how the test suite caught it.

namespace MotsSupplierPortal.Application.Suppliers;

public sealed record OfferingDto(
    Guid Id, string NameAr, string NameEn, string? Description,
    string CategoryCode, string UnitOfMeasureCode, decimal? PriceAmount, string? CurrencyCode, bool IsActive,
    IReadOnlyDictionary<string, string>? Attributes,
    long RowVersion = 0);

public interface IGetOfferingHandler
{
    Task<OfferingDto?> HandleAsync(Guid offeringId, CancellationToken ct);
}

public sealed record CreateOfferingCommand(
    string NameAr, string NameEn, string? Description,
    string CategoryCode, string UnitOfMeasureCode, decimal? PriceAmount, string? CurrencyCode,
    IReadOnlyDictionary<string, string>? Attributes);

public sealed record UpdateOfferingCommand(
    Guid OfferingId, string NameAr, string NameEn, string? Description,
    string CategoryCode, string UnitOfMeasureCode, decimal? PriceAmount, string? CurrencyCode,
    IReadOnlyDictionary<string, string>? Attributes);

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

public sealed record BuyerOfferingSearchResultDto(
    Guid Id, string SupplierReferenceCode, string SupplierDisplayNameAr, string SupplierDisplayNameEn,
    string NameAr, string NameEn, string? Description, string CategoryCode, string UnitOfMeasureCode,
    decimal? PriceAmount, string? CurrencyCode, IReadOnlyDictionary<string, string>? Attributes);

public interface ISearchBuyerOfferingsHandler
{
    Task<IReadOnlyList<BuyerOfferingSearchResultDto>> HandleAsync(string? categoryCode, string? query, CancellationToken ct);
}
