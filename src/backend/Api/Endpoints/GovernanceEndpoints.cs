using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Governance;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Api.Endpoints;

/// <summary>
/// EPIC-18/FR-DSH-005/SCR-600: the Ministry's governance overview.
///
/// <para>Before this, <c>ministry_viewer</c> held an EMPTY permission set - the persona could log in
/// and reach nothing at all, which is the EPIC-11 defect at persona scale.</para>
/// </summary>
/// <summary>What the Ministry's two list endpoints accept as filter values. Named here for the reason
/// ReviewQueueFilterValues gives: an unrecognised value that reaches a handler applies no predicate at all,
/// and an unfiltered list that reads as a filtered one is worse than an error.</summary>
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
        // governance.read, not rfq.read or report.read: both of those are row-scoped to one
        // organization, and this read deliberately is not. A cross-organization read must be reachable
        // only by the persona whose whole purpose is to cross organizations.
        .RequirePermission(Permissions.GovernanceRead)
        .WithTags("Ministry")
        .WithName("GetGovernanceOverview");

        // ── SCR-601, 602, 603, 606 ────────────────────────────────────────────────────────────────
        //
        // The four screens BRULE-087's aggregate-only default refused for two batches, built under D-66.
        //
        // Read that decision before widening anything here. D-57 relayed that the Ministry may see commercial
        // figures and required written sign-off first - a name, a date and a scope. D-66 records that these
        // shipped WITHOUT it, at the product owner's direction, to the widest scope offered: live tenders
        // included, per-bidder values shown. A `ministry_viewer` can therefore read what each named supplier
        // has bid on a tender that is still open.
        //
        // governance.read gates all four, like every other Ministry read: these routes skip organization
        // scoping deliberately, and a route that skips row-scoping must be reachable only by the persona
        // whose whole purpose is to cross organizations.

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

        // SCR-604: category and sector coverage. Squarely inside BRULE-086's aggregate grant - every figure
        // is a count, so this is one of the two Ministry screens that were never refused under BRULE-087 and
        // were absent anyway (T-100). It does not touch the commercial-visibility question D-57 is waiting on
        // a signature for, because no figure here is commercial.
        app.MapGet("/api/v1/ministry/categories", async (
            IGetCategoryCoverageHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .RequirePermission(Permissions.GovernanceRead)
        .WithTags("Ministry")
        .WithName("GetCategoryCoverage");
    }
}
