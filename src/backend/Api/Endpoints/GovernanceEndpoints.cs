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
