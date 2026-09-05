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
    }
}
