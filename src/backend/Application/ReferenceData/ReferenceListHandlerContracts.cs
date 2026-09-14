// What the five public reference-list reads are called, one interface each.
//
// They are separate interfaces rather than one taking a table name, because each returns its own
// shape and each is resolved by the route that serves it. A single interface taking a table would
// move the choice of table from the compiler to a string.
//
// The implementations are in Infrastructure. These are only the names the routes ask for.

namespace MotsSupplierPortal.Application.ReferenceData;

public interface IGetCategoriesHandler
{
    Task<IReadOnlyList<CategoryDto>> HandleAsync(CancellationToken ct);
}

public interface IGetCurrenciesHandler
{
    Task<IReadOnlyList<CurrencyDto>> HandleAsync(CancellationToken ct);
}

public interface IGetRegionsHandler
{
    Task<IReadOnlyList<RegionDto>> HandleAsync(CancellationToken ct);
}

public interface IGetUnitsOfMeasureHandler
{
    Task<IReadOnlyList<UnitOfMeasureDto>> HandleAsync(CancellationToken ct);
}

public interface IGetIncotermsHandler
{
    Task<IReadOnlyList<IncotermDto>> HandleAsync(CancellationToken ct);
}
