// What the ministry's four oversight reads are called: the tender list, the supplier list, the award
// analytics, and one tender in detail.
//
// All four cross organizations, which nothing else in this API does, so all four are gated on the governance
// permission rather than on reading tenders or reports. Both of those are scoped to one organization, and a
// read that skips that scoping must be reachable only by the persona whose whole purpose is to cross it.

namespace MotsSupplierPortal.Application.Governance;

using MotsSupplierPortal.Application.Common;

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
