// Returns a page of a list, attaching the warning header the contract requires when a caller asked for
// more rows than the documented ceiling.
//
// The page size defaults to twenty, the minimum is one and the maximum is a hundred. A larger request is
// served at the ceiling and warned about rather than refused.
//
// The header's exact shape is a choice rather than a transcription. The contract requires that a warning
// is sent and says nothing about its code or its wording. The relevant standard defines the syntax as a
// code, an agent and a quoted text, and reserves one code for a miscellaneous warning whose text is meant
// for a person to read. That is the closest standard fit, and a dash is the conventional agent
// placeholder when the sender is the server itself. Flagged as a documented silence.

namespace MotsSupplierPortal.Api.Endpoints;

using MotsSupplierPortal.Application.Common;

internal static class ListResponse
{
    public static IResult Ok<T>(HttpContext context, ListEnvelope<T> page, int? requestedPageSize)
    {
        if (ListEnvelope<T>.WasClamped(requestedPageSize))
        {
            context.Response.Headers.Append(
                "Warning", $"199 - \"pageSize clamped to {ListEnvelope<T>.MaxPageSize}\"");
        }

        return Results.Ok(page);
    }
}
