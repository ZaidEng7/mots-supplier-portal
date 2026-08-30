using System.Text;

namespace MotsSupplierPortal.Application.Auth;

/// <summary>
/// MSP-84: keyset cursor for the sessions list, ordered descending by (CreatedAt, FamilyId) -
/// newest session first, matching the list's existing order. Same encode shape as
/// Application/Audit/AuditCursor.
/// </summary>
public readonly record struct SessionCursor(DateTimeOffset CreatedAt, Guid FamilyId)
{
    public string Encode() =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{CreatedAt.UtcTicks:D19}:{FamilyId:N}"));

    public static bool TryDecode(string? value, out SessionCursor cursor)
    {
        cursor = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        Span<byte> buffer = new byte[value.Length];
        if (!Convert.TryFromBase64String(value, buffer, out var written)) return false;

        var parts = Encoding.UTF8.GetString(buffer[..written]).Split(':');
        if (parts.Length != 2
            || !long.TryParse(parts[0], out var ticks)
            || !Guid.TryParseExact(parts[1], "N", out var familyId))
        {
            return false;
        }

        if (ticks < 0 || ticks > DateTimeOffset.MaxValue.UtcTicks) return false;

        cursor = new SessionCursor(new DateTimeOffset(ticks, TimeSpan.Zero), familyId);
        return true;
    }
}
