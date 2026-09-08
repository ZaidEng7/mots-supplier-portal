using System.Text;

namespace MotsSupplierPortal.Application.Governance;

/// <summary>
/// SCR-602's keyset cursor: newest tender first, with an Id tie-break.
///
/// <para>Descending, unlike the review queue's, because the two lists are read from opposite ends. A queue is
/// worked from the case that has waited longest; a monitor is read from what happened today.</para>
///
/// <para>An unparseable token yields the first page rather than an error, matching every other cursor here
/// (AuditCursor, SessionCursor, ReviewQueueCursor, SupplierUserCursor, RfqListCursor, SupplierDirectoryCursor).
/// The property that matters is that a hostile token never reaches the database.</para>
/// </summary>
public readonly record struct MinistryRfqCursor(DateTimeOffset CreatedAt, Guid Id)
{
    public string Encode() =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{CreatedAt.UtcTicks:D19}:{Id:N}"));

    public static bool TryDecode(string? value, out MinistryRfqCursor cursor)
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

        cursor = new MinistryRfqCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return true;
    }
}
