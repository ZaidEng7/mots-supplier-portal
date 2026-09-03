using MotsSupplierPortal.Api.Concurrency;
using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Api.Authorization;

/// <summary>
/// Reads the caller's expected row version from the standard <c>If-Match</c> header (MSP-65, §8.1).
///
/// <para><b>The wire format changed with §8.1.</b> This used to accept a bare decimal
/// (<c>If-Match: 12345</c>) because that is what the DTO exposed; §8.1 specifies the version
/// base64url-encoded inside an entity-tag, and <see cref="ETag"/> is now the only reader. The old
/// format is deliberately NOT still accepted - two live wire formats for one header is how the
/// wrong one becomes permanent, and the only client moves in the same change.</para>
/// </summary>
public sealed class HttpConcurrencyContext(IHttpContextAccessor accessor) : IConcurrencyContext
{
    public uint? ExpectedRowVersion
    {
        get
        {
            // Read from the endpoint filter's output, not straight from the header. Only routes
            // that declare RequireIfMatch() participate in §8.1's contract, and the filter has
            // already validated the value by the time it lands here - so a header sent to any other
            // endpoint is inert rather than a precondition nobody promised.
            return accessor.HttpContext?.Items[ConcurrencyEndpoints.ExpectedVersionKey] as uint?;
        }
    }
}
