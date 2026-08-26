using MotsSupplierPortal.Application.Suppliers;

namespace MotsSupplierPortal.Api.Endpoints;

public static class SupplierEndpoints
{
    public static void MapSupplierEndpoints(this IEndpointRouteBuilder app)
    {
        // Authenticated + row-scoped (STORY-01.8.1); no specific permission needed - any
        // authenticated supplier user may look up their own supplier record.
        app.MapGet("/api/v1/suppliers/{referenceCode}", async (
            string referenceCode,
            IGetSupplierHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(referenceCode, ct);

            return result switch
            {
                GetSupplierResult.Found f => Results.Ok(f.Supplier),
                GetSupplierResult.NotFoundOrOutOfScope => Results.NotFound(),
                _ => Results.Problem(),
            };
        })
        .RequireAuthorization()
        .WithName("GetSupplier")
        .WithTags("Suppliers");
    }
}
