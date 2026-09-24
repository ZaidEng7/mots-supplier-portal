// The place a caller got to in a feed, in a form that survives a round trip through their loader.
//
// IT IS OPAQUE ON PURPOSE. A cursor that reads as "SUP-2026-000123" invites a caller to construct one, and the
// day the sort key changes every one they built stops meaning what it did. Base64 says "this came from us,
// hand it back unchanged" without pretending to be a secret: anyone who decodes it finds a reference code they
// were already reading in the rows.
//
// IT HOLDS THE SORT KEY, WHICH IS WHY IT IS A LIST. Feed 1 is ordered by one immutable code and feed 4 by two,
// so the cursor carries however many parts that feed sorts on, in order.
//
// A CURSOR THAT DOES NOT DECODE IS NOT AN ERROR HERE. This answers false and lets the caller decide; the feeds
// treat it as a refusal rather than silently starting from the beginning, because a nightly job that quietly
// restarts from row one after a typo will load the whole registry and report success.
//
// THE SEPARATOR IS A UNIT SEPARATOR rather than a comma or a colon, because the parts are reference codes and
// the day one of them contains the separator is the day paging silently skips rows. Nothing anybody types
// contains 0x1F.

namespace MotsSupplierPortal.Application.Exports;

using System.Text;

public static class FeedCursor
{
    private const char Separator = '';

    public static string Encode(params string[] parts) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join(Separator, parts)));

    public static bool TryDecode(string? cursor, int expectedParts, out string[] parts)
    {
        parts = [];

        if (string.IsNullOrWhiteSpace(cursor)) return false;

        Span<byte> buffer = new byte[cursor.Length];
        if (!Convert.TryFromBase64String(cursor, buffer, out var written)) return false;

        var decoded = Encoding.UTF8.GetString(buffer[..written]).Split(Separator);
        if (decoded.Length != expectedParts) return false;

        parts = decoded;
        return true;
    }
}
