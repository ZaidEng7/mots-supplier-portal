// The ministry's governance screens: a read across every buying organization, which nothing else in this API
// does.
//
// Before these existed the ministry's persona held an empty permission set and could sign in and reach
// nothing at all.
//
// Every route here is gated on the governance permission rather than on reading tenders or reading reports.
// Both of those are scoped to one organization, and this read deliberately is not. A route that skips row
// scoping must be reachable only by the persona whose whole purpose is to cross organizations.
//
// Filter values are named in one place for the same reason they are elsewhere: an unrecognised value reaching
// a handler applies no filter at all, and an unfiltered list that reads as a filtered one is worse than an
// error.
//
//
// READ THIS BEFORE WIDENING ANYTHING HERE
//
// Four of these screens show commercial figures, and they shipped without the written sign-off that was asked
// for first.
//
// The default position was that the ministry sees aggregate figures only. A relayed decision said the ministry
// may see commercial figures and required written sign-off first: a name, a date and a scope. A later recorded
// decision notes that these four shipped without it, at the product owner's direction, to the widest scope
// offered: live tenders included and per-bidder values shown.
//
// So a ministry viewer can read what each named supplier has bid on a tender that is still open. That is
// recorded here rather than buried, because it is the fact somebody widening this surface needs to know.
//
// The category and sector coverage screen is different and was never in question. Every figure on it is a
// count, so it sits squarely inside the aggregate-only grant and does not touch the commercial-visibility
// question at all.

namespace MotsSupplierPortal.Api.Endpoints;

using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Governance;
using MotsSupplierPortal.Domain.Identity;

public static class MinistryFilterValues
{
    public static readonly IReadOnlySet<string> RfqStates = new HashSet<string>(StringComparer.Ordinal)
    {
        "Draft", "InternalReview", "Approved", "Published", "SubmissionOpen", "SubmissionClosed",
        "UnderEvaluation", "Clarification", "Shortlisting", "Recommendation", "AwardApproval", "Awarded",
        "Completed", "Cancelled",
    };

    public static readonly IReadOnlySet<string> LifecycleStates = new HashSet<string>(StringComparer.Ordinal)
    {
        "None", "Active", "Suspended", "Deactivated",
    };
}

public static class GovernanceEndpoints
{
    public static void MapGovernanceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/ministry/overview", async (
            IGetGovernanceOverviewHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .RequirePermission(Permissions.GovernanceRead)
        .WithTags("Ministry")
        .WithName("GetGovernanceOverview");

        app.MapGet("/api/v1/ministry/rfqs", async (
            string? cursor, int? pageSize, string? withCount, string? state, string? q,
            HttpContext httpContext, IListMinistryRfqsHandler handler, CancellationToken ct) =>
        {
            if (!FilterValues.TryParseBoolFilter(withCount, out _, out var badWithCount))
            {
                return FilterValues.InvalidFilterValue("withCount", badWithCount!);
            }

            if (!FilterValues.IsAllowed(state, MinistryFilterValues.RfqStates, out var badState))
            {
                return FilterValues.InvalidFilterValue("state", badState!);
            }

            return ListResponse.Ok(httpContext,
                await handler.HandleAsync(cursor, pageSize, FilterValues.BoolOrFalse(withCount), state, q, ct),
                pageSize);
        })
        .RequirePermission(Permissions.GovernanceRead)
        .WithListQuery(ListQueryPolicy.Create("-createdAt", ["createdAt"], "state", "q"))
        .WithTags("Ministry")
        .WithName("ListMinistryRfqs");

        app.MapGet("/api/v1/ministry/suppliers", async (
            string? cursor, int? pageSize, string? withCount, string? lifecycleState, string? q,
            HttpContext httpContext, IListMinistrySuppliersHandler handler, CancellationToken ct) =>
        {
            if (!FilterValues.TryParseBoolFilter(withCount, out _, out var badWithCount))
            {
                return FilterValues.InvalidFilterValue("withCount", badWithCount!);
            }

            if (!FilterValues.IsAllowed(lifecycleState, MinistryFilterValues.LifecycleStates, out var badState))
            {
                return FilterValues.InvalidFilterValue("lifecycleState", badState!);
            }

            return ListResponse.Ok(httpContext,
                await handler.HandleAsync(cursor, pageSize, FilterValues.BoolOrFalse(withCount), lifecycleState, q, ct),
                pageSize);
        })
        .RequirePermission(Permissions.GovernanceRead)
        .WithListQuery(ListQueryPolicy.Create("displayNameEn", ["displayNameEn"], "lifecycleState", "q"))
        .WithTags("Ministry")
        .WithName("ListMinistrySuppliers");

        app.MapGet("/api/v1/ministry/awards", async (
            IGetMinistryAwardAnalyticsHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .RequirePermission(Permissions.GovernanceRead)
        .WithTags("Ministry")
        .WithName("GetMinistryAwardAnalytics");

        app.MapGet("/api/v1/ministry/rfqs/{referenceCode}", async (
            string referenceCode, IGetMinistryRfqDetailHandler handler, CancellationToken ct) =>
        {
            var detail = await handler.HandleAsync(referenceCode, ct);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        })
        .RequirePermission(Permissions.GovernanceRead)
        .WithTags("Ministry")
        .WithName("GetMinistryRfqDetail");

        app.MapGet("/api/v1/ministry/categories", async (
            IGetCategoryCoverageHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .RequirePermission(Permissions.GovernanceRead)
        .WithTags("Ministry")
        .WithName("GetCategoryCoverage");
    }
}
