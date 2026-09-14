// The opaque token a caller hands back to get the next page of a list ordered by time.
//
// It carries the moment and the identifier of the last row the caller received. The query then asks
// for rows after that point, which is why this is a cursor rather than a page number.
//
//
// WHY KEYSET RATHER THAN COUNTING ROWS TO SKIP
//
// Skipping rows makes the database walk and discard every row it skips, so page four hundred costs
// four hundred pages of work and gets slower forever. A cursor seeks straight to the point and costs
// the same at any depth.
//
// It also survives writes. Rows are inserted into these tables while somebody is paging: a supplier
// can register between two page fetches of the review queue. Counting rows to skip drops or repeats
// rows at the page boundary when that happens, and a cursor does not.
//
// The audit trail is the sharpest case, because it is append-only and kept indefinitely, so it is the
// one table guaranteed to grow without bound.
//
//
// WHY THE IDENTIFIER IS PART OF THE KEY
//
// A timestamp alone is not unique. Several audit rows share one routinely, because a single request
// writes several of them under one correlation identifier. Paging on a key that is not unique silently
// drops or repeats rows at the boundary.
//
// The identifiers are time-ordered by construction, so they sort consistently with the timestamp
// rather than shuffling ties arbitrarily.
//
//
// WHY ONE TYPE RATHER THAN ONE PER LIST
//
// There were five, for the audit trail, the tender list, the ministry's tender list, the sessions
// list and the review queue. Their encoding was character-for-character identical and only the field
// names differed, so it was one behaviour written five times and a fix to any one of them would not
// have reached the other four.
//
// The sixth cursor, for a supplier's own users, keys on an email address rather than a time and stays
// its own type.
//
// The direction is not part of the cursor. Whether a list reads forwards or backwards is a property of
// the query, and each query already states it: the review queue asks for rows after this point,
// because a reviewer works the oldest first, and the audit trail asks for rows before it.
//
//
// WHY IT IS OPAQUE
//
// Callers must treat it as a token to hand back rather than a value to construct, so the shape of the
// encoding stays ours to change.
//
// Decoding never throws. Anything that is not a cursor this type produced, a truncated token, an
// invented one, or a hostile one, is refused and the caller gets the first page rather than an error.

namespace MotsSupplierPortal.Application.Common;

using System.Text;

public readonly record struct KeysetCursor(DateTimeOffset At, Guid Id)
{
    public string Encode() =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{At.UtcTicks:D19}:{Id:N}"));

    public static bool TryDecode(string? value, out KeysetCursor cursor)
    {
        cursor = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        Span<byte> buffer = new byte[value.Length];
        if (!Convert.TryFromBase64String(value, buffer, out var written)) return false;

        var parts = Encoding.UTF8.GetString(buffer[..written]).Split(':');
        if (parts.Length != 2
            || !long.TryParse(parts[0], out var ticks)
            || !Guid.TryParseExact(parts[1], "N", out var id))
        {
            return false;
        }

        if (ticks < 0 || ticks > DateTimeOffset.MaxValue.UtcTicks) return false;

        cursor = new KeysetCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return true;
    }
}
