using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Application.Governance;

public interface IListMinistryRfqsHandler
{
    Task<ListEnvelope<MinistryRfqRowDto>> HandleAsync(string? cursor, int? limit, bool withCount, string? state, string? q, CancellationToken ct);
}

public interface IListMinistrySuppliersHandler
{
    Task<ListEnvelope<MinistrySupplierRowDto>> HandleAsync(string? cursor, int? limit, bool withCount, string? lifecycleState, string? q, CancellationToken ct);
}

public interface IGetMinistryAwardAnalyticsHandler
{
    Task<MinistryAwardAnalyticsDto> HandleAsync(CancellationToken ct);
}

public interface IGetMinistryRfqDetailHandler
{
    Task<MinistryRfqDetailDto?> HandleAsync(string referenceCode, CancellationToken ct);
}
