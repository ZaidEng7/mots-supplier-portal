namespace MotsSupplierPortal.Application.Reference;

public sealed record CategoryDto(Guid Id, string Code, string NameAr, string NameEn);

public interface IGetCategoriesHandler
{
    Task<IReadOnlyList<CategoryDto>> HandleAsync(CancellationToken ct);
}
