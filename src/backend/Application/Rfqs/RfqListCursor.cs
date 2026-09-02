using System.Text;

namespace MotsSupplierPortal.Application.Rfqs;

/// <summary>
/// Keyset cursor for both RFQ lists: the (CreatedAt, Id) of the last row a caller received.
///
/// <para>API-ARCHITECTURE.md §6.1 makes RFQs a cursor-default collection and describes the token as
/// an *"Opaque base64url cursor [that] encodes the last sort tuple + direction"*. Same shape and
/// same reasoning as <c>AuditCursor</c>, which this deliberately mirrors rather than inventing a
/// second cursor idiom.</para>
///
/// <para><b>Why the tie-break on Id.</b> <c>CreatedAt</c> alone is not unique - two RFQs authored in
/// the same tick collide, and a seeded test fixture creates several in a loop, which is exactly
/// where it bites. Paging on a non-unique key silently drops or repeats rows at page boundaries:
/// the row that "already came" on page one reappears on page two, or is skipped entirely. Id is
/// GUIDv7, so it is itself time-ordered and breaks ties consistently with CreatedAt rather than
/// shuffling them.</para>
///
/// <para><b>Malformed cursors return page one, not an error.</b> The contract names no error type
/// for a bad cursor (§7.1's catalog has no invalid-cursor slug), and every existing cursor in this
/// codebase is total in the same way. Reported as a documented silence rather than resolved by
/// inventing a 422 the contract does not define.</para>
/// </summary>
public readonly record struct RfqListCursor(DateTimeOffset CreatedAt, Guid Id)
{
    public string Encode() =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{CreatedAt.UtcTicks:D19}:{Id:N}"));

    public static bool TryDecode(string? value, out RfqListCursor cursor)
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

        cursor = new RfqListCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return true;
    }
}
