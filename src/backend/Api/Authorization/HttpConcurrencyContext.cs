using Microsoft.Net.Http.Headers;
using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Api.Authorization;

/// <summary>
/// Reads the caller's expected row version from the standard <c>If-Match</c> header (MSP-65).
/// Accepts the value both bare (<c>If-Match: 12345</c>) and quoted as a proper ETag
/// (<c>If-Match: "12345"</c>), since HTTP requires the quotes but hand-written clients routinely
/// omit them and silently losing the guard over a punctuation detail is exactly the failure mode
/// this ticket exists to remove.
/// </summary>
public sealed class HttpConcurrencyContext(IHttpContextAccessor accessor) : IConcurrencyContext
{
    public uint? ExpectedRowVersion
    {
        get
        {
            var raw = accessor.HttpContext?.Request.Headers[HeaderNames.IfMatch].ToString();
            if (string.IsNullOrWhiteSpace(raw) || raw == "*") return null;

            var unquoted = raw.Trim().Trim('"');
            return uint.TryParse(unquoted, out var version) ? version : null;
        }
    }
}
