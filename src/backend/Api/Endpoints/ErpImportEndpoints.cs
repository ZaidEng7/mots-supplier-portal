// The supplier import: /api/v1/admin/erp-import/preview forecasts it, /api/v1/admin/erp-import/run performs it.
//
// THE TWO SIT TOGETHER ON PURPOSE. A preview whose write lives somewhere else invites somebody to bookmark one
// and forget the other, and the forecast is only worth anything if the thing it forecasts is one click away.
//
// THE PREVIEW IS A POST ALTHOUGH IT CHANGES NO SUPPLIER - it writes only the audit row saying who looked. Two
// reasons, and the weaker one is that it takes minutes and calls another ministry's server, which is not what a
// cache or a link-prefetcher should be free to trigger. The stronger one is that it is the front half of an action,
// and it sits at the same verb as the run it previews for the reason above.
//
// THE WHOLE REPORT COMES BACK IN ONE RESPONSE, unpaged. Eighty rows is a list a person reads before agreeing to
// an import, and the rows that matter are the unusual ones - a page boundary is exactly where those would hide.
//
// A MISSING IMPORT PASSWORD IS ALSO A 503, and it is a separate exception from a missing ERP because it names a
// different setting. The run refuses before it writes anything rather than discovering it on the first account.
//
// A SECOND IMPORT WHILE ONE IS RUNNING IS A 409. Two overlapping runs would both create the same new supplier, so
// the second is refused rather than queued - see ErpImportLock.
//
// AN UNCONFIGURED INTEGRATION IS 503 AND SAYS WHICH SETTING IS MISSING. It is not a 500, because nothing is
// broken, and it is not an empty report, because "0 suppliers, nothing to import" reads like success and somebody
// would forward it.
//
// A REFUSAL FROM THE ERP IS 502, NOT 500. The portal is working; the system it asked is not, or the credential
// it was given has been narrowed. Reporting our own fault for their outage sends the next person to read our logs.
// The ERP's own status and exception type are carried through in the detail, because "403 PermissionError" names
// the fix - grant the credential the record type - and a generic failure does not.

namespace MotsSupplierPortal.Api.Endpoints;

using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Integration;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Integration.Erp;

public static class ErpImportEndpoints
{
    public static IEndpointRouteBuilder MapErpImportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/admin/erp-import/preview", async (
            IPreviewErpImportHandler handler,
            CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await handler.HandleAsync(ct));
            }
            catch (ErpNotConfiguredException notConfigured)
            {
                return Results.Problem(
                    title: "The ERP integration is not configured.",
                    detail: notConfigured.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (ErpRequestException failure)
            {
                return Results.Problem(
                    title: "The ERP could not be read.",
                    detail: failure.Message,
                    statusCode: StatusCodes.Status502BadGateway);
            }
        })
        .RequirePermission(Permissions.SupplierImportRun)
        .WithName("PreviewErpImport")
        .WithTags("ErpImport");

        app.MapPost("/api/v1/admin/erp-import/run", async (
            IRunErpImportHandler handler,
            CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await handler.HandleAsync(ErpImportTrigger.Manual, ct));
            }
            catch (ErpImportBusyException busy)
            {
                return Results.Problem(
                    title: "An import is already running.",
                    detail: busy.Message,
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (ErpNotConfiguredException notConfigured)
            {
                return Results.Problem(
                    title: "The ERP integration is not configured.",
                    detail: notConfigured.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (ErpImportNotConfiguredException notConfigured)
            {
                return Results.Problem(
                    title: "The import is not configured.",
                    detail: notConfigured.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (ErpRequestException failure)
            {
                return Results.Problem(
                    title: "The ERP could not be read.",
                    detail: failure.Message,
                    statusCode: StatusCodes.Status502BadGateway);
            }
        })
        .RequirePermission(Permissions.SupplierImportRun)
        .WithName("RunErpImport")
        .WithTags("ErpImport");

        return app;
    }
}
