namespace MotsSupplierPortal.Application.Reference;

public sealed record CurrencyDto(Guid Id, string Code, string NameAr, string NameEn);

public interface IGetCurrenciesHandler
{
    Task<IReadOnlyList<CurrencyDto>> HandleAsync(CancellationToken ct);
}
