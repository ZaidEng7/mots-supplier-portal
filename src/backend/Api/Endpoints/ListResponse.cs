using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Api.Endpoints;

/// <summary>
/// Returns a list envelope, attaching the `Warning` header API-ARCHITECTURE.md §6.1 requires when a
/// caller asked for more rows than the documented ceiling: *"`pageSize` default 20, min 1, max 100
/// (`&gt; 100` → clamped + `Warning` header)"*.
///
/// <para><b>The header's format is a choice, not a transcription.</b> §6.1 mandates *that* a Warning
/// header is sent and says nothing about its code or text (§6.4 mentions one too, equally
/// unspecified). RFC 7234 §5.5 defines the syntax as `warn-code warn-agent "warn-text"`, and
/// reserves <b>199</b> for a miscellaneous warning whose text is meant for a human. That is the
/// closest standard fit; `-` is the conventional agent placeholder when the sender is the origin
/// server. Flagged in the batch report as a documented silence.</para>
/// </summary>
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
