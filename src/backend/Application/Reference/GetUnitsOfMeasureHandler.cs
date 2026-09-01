namespace MotsSupplierPortal.Application.Reference;

public sealed record UnitOfMeasureDto(Guid Id, string Code, string NameAr, string NameEn);

public interface IGetUnitsOfMeasureHandler
{
    Task<IReadOnlyList<UnitOfMeasureDto>> HandleAsync(CancellationToken ct);
}
