// Reads the version the caller expects a record to be at, off the standard precondition header.
//
// The wire format is the version encoded inside an entity tag, and the ETag helper is the only thing
// that reads it. It used to accept a bare number, because that is what the response exposed at the
// time. The old format is deliberately no longer accepted: two live formats for one header is how the
// wrong one becomes permanent, and the only client moved in the same change.
//
// The value is read from what the route's own filter produced rather than straight from the header.
// Only routes that declare the precondition take part in this contract, and by the time it lands here
// the filter has already checked it, so the same header sent to any other endpoint is inert rather
// than a promise nobody made.

namespace MotsSupplierPortal.Api.Authorization;

using MotsSupplierPortal.Api.Concurrency;
using MotsSupplierPortal.Application.Common;

public sealed class HttpConcurrencyContext(IHttpContextAccessor accessor) : IConcurrencyContext
{
    public uint? ExpectedRowVersion
    {
        get
        {
            return accessor.HttpContext?.Items[ConcurrencyEndpoints.ExpectedVersionKey] as uint?;
        }
    }
}
