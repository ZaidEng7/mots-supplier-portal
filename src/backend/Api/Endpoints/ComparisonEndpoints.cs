using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Comparison;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Api.Endpoints;

/// <summary>FEAT-12.1..12.4/FR-CMP-001..004: read-only, no request body, no query-string
/// sort/filter parameter - see GetComparisonHandler's own doc comment on why that absence is
/// itself the mitigation for "can the query be coaxed into leaking gated data".</summary>
public static class ComparisonEndpoints
{
    public static void MapComparisonEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/rfqs/{referenceCode}/comparison", async (
            string referenceCode, IGetComparisonHandler handler, CancellationToken ct) =>
        {
            var comparison = await handler.HandleAsync(referenceCode, ct);
            return comparison is null ? Results.NotFound() : Results.Ok(comparison);
        })
        .RequirePermission(Permissions.ComparisonView)
        .WithTags("Comparison")
        .WithName("GetComparison");
    }
}
