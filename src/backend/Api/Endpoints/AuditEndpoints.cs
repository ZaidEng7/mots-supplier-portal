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
    }
}
