using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Audit;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Api.Endpoints;

public static class AuditEndpoints
{
    public static void MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/audit/{aggregateId:guid}", async (
            Guid aggregateId,
            IGetAuditLogHandler handler,
            CancellationToken ct) =>
        {
            var entries = await handler.HandleAsync(aggregateId, ct);
            return Results.Ok(entries);
        })
        .RequirePermission(Permissions.AuditRead)
        .WithName("GetAuditLog")
        .WithTags("Audit");

        // FR-AUD-003: "suppliers see their own activity trail". Intentionally NOT gated on
        // audit.read - that permission governs reading *other* aggregates' trails. Reading your
        // own supplier's history needs only an authenticated, supplier-scoped session; the handler
        // resolves the scope from the token and cannot be pointed at anyone else.
        // Keyset-paged (MSP-66, NFR-PERF-006). `cursor` is opaque - hand back the nextCursor from
        // the previous response; anything else is treated as "start from the beginning" rather than
        // as an error.
        app.MapGet("/api/v1/suppliers/me/audit", async (
            string? cursor,
            int? limit,
            IGetAuditLogHandler handler,
            CancellationToken ct) =>
        {
            var page = await handler.HandleOwnTrailAsync(cursor, limit, ct);
            return Results.Ok(page);
        })
        .RequireAuthorization()
        .WithName("GetOwnSupplierAuditTrail")
        .WithTags("Audit");
    }
}
