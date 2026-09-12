namespace MotsSupplierPortal.Application.Reference;

/// <summary>T-072. Shaped like every other reference DTO, so the SPA's existing select renders it
/// without a special case.</summary>
public sealed record IncotermDto(Guid Id, string Code, string NameAr, string NameEn);

public interface IGetIncotermsHandler
{
    Task<IReadOnlyList<IncotermDto>> HandleAsync(CancellationToken ct);
}
