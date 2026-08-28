using System.Buffers.Text;
using System.Text;

namespace MotsSupplierPortal.Application.Audit;

/// <summary>
/// Keyset cursor for the audit trail: the (OccurredAt, Id) of the last row a caller received.
///
/// <para><b>Why keyset rather than offset here.</b> The audit log is append-only and retained
/// indefinitely (ASM-085), so it is the one table guaranteed to grow without bound. OFFSET makes
/// the database walk and discard every skipped row, so page 400 costs 400 pages of work and gets
/// slower forever. Keyset seeks straight to the cursor and costs the same at any depth. Offset is
/// fine for bounded-ish lists like the review queue; it is the wrong choice for precisely the table
/// that never stops growing.</para>
///
/// <para><b>Why the tie-break on Id.</b> OccurredAt alone is not unique - several audit rows share
/// a timestamp routinely, because one request now writes several rows under one correlation id.
/// Paging on a non-unique key silently drops or repeats rows at page boundaries. Id is GUIDv7, so
/// it is itself time-ordered and sorts consistently with OccurredAt rather than shuffling ties
/// arbitrarily.</para>
///
/// <para>The encoding is opaque on purpose: callers must treat it as a token to hand back, not a
/// value to construct. A malformed or hostile cursor is rejected rather than interpreted.</para>
/// </summary>
public readonly record struct AuditCursor(DateTimeOffset OccurredAt, Guid Id)
{
    public string Encode() =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{OccurredAt.UtcTicks:D19}:{Id:N}"));

    /// <summary>Returns false for anything that is not a cursor this class produced. Deliberately
    /// total: a caller pasting a truncated or invented token gets page one, not a 500.</summary>
    public static bool TryDecode(string? value, out AuditCursor cursor)
    {
        cursor = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        Span<byte> buffer = new byte[value.Length];
        if (!Convert.TryFromBase64String(value, buffer, out var written))
        {
            return false;
        }

        var parts = Encoding.UTF8.GetString(buffer[..written]).Split(':');
        if (parts.Length != 2
            || !long.TryParse(parts[0], out var ticks)
            || !Guid.TryParseExact(parts[1], "N", out var id))
        {
            return false;
        }

        if (ticks < 0 || ticks > DateTimeOffset.MaxValue.UtcTicks)
        {
            return false;
        }

        cursor = new AuditCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return true;
    }
}
