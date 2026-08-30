using System.Buffers.Text;
using System.Text;

namespace MotsSupplierPortal.Application.Suppliers;

/// <summary>
/// MSP-84: keyset cursor for the review queue, same shape as Application/Audit/AuditCursor and
/// for the same reason - offset paging on a table rows are actively inserted into (a supplier can
/// register between two page fetches) drops or repeats rows at the page boundary. The queue
/// orders ascending (oldest submission first, the order a reviewer should work it), so the
/// cursor predicate is "greater than", the mirror image of AuditCursor's descending "less than".
/// </summary>
public readonly record struct ReviewQueueCursor(DateTimeOffset CreatedAt, Guid Id)
{
    public string Encode() =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{CreatedAt.UtcTicks:D19}:{Id:N}"));

    public static bool TryDecode(string? value, out ReviewQueueCursor cursor)
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

        cursor = new ReviewQueueCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return true;
    }
}
