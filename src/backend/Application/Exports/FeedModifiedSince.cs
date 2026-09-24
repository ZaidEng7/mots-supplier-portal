// Reading the incremental filter a nightly job sends, and refusing one that cannot be read.
//
// A TIMESTAMP THAT DOES NOT PARSE IS A REFUSAL, NEVER AN EMPTY FILTER. This is the whole reason this is a
// method rather than three characters of parsing at the call site. If a malformed value quietly became "no
// filter", a job with a bug in how it formats dates would pull the entire registry every night and report
// success: more rows than expected is not a shape anyone monitors, and the dashboard would be correct, so
// nothing would ever surface it. The opposite mistake - treating it as "everything since the epoch" - has the
// same effect. So it is a 400, which a job's error handling has to notice.
//
// THE OFFSET IS REQUIRED IN EFFECT: a value with no zone is read as UTC rather than as the server's local time.
// Their requirements ask for timestamps with a time zone and this product answers in UTC, so a caller echoing
// back a value we sent is already right. A caller that strips the Z would otherwise silently shift its window
// by however many hours the server happens to sit from Greenwich, which in Damascus is three - three hours of
// changes missed on every pull, or re-sent on every pull, depending on the sign.
//
// ABSENT IS NOT AN ERROR. No filter means the whole feed, which is what the first load of any incremental
// pipeline does.

namespace MotsSupplierPortal.Application.Exports;

using System.Globalization;

public static class FeedModifiedSince
{
    public static bool TryParse(string? value, out DateTimeOffset? modifiedSince)
    {
        modifiedSince = null;

        if (string.IsNullOrWhiteSpace(value)) return true;

        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var parsed))
        {
            return false;
        }

        modifiedSince = parsed;
        return true;
    }
}
