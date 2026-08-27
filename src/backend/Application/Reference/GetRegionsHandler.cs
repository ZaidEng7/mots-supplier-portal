namespace MotsSupplierPortal.Application.Reference;

public sealed record RegionDto(Guid Id, string Code, string NameAr, string NameEn);

public interface IGetRegionsHandler
{
    Task<IReadOnlyList<RegionDto>> HandleAsync(CancellationToken ct);
}
