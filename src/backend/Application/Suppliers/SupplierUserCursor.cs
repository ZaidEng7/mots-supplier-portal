using System.Text.Json;

namespace MotsSupplierPortal.Application.Suppliers;

/// <summary>
/// MSP-84: keyset cursor for the team-members list, ordered ascending by (Email, Id) - matching
/// the list's existing display order (Email). JSON+Base64 rather than the AuditCursor/
/// ReviewQueueCursor colon-delimited format because Email is arbitrary text and cannot safely
/// share a fixed delimiter the way a numeric timestamp can.
/// </summary>
public readonly record struct SupplierUserCursor(string Email, Guid Id)
{
    public string Encode() =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(this));

    public static bool TryDecode(string? value, out SupplierUserCursor cursor)
    {
        cursor = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        try
        {
            Span<byte> buffer = new byte[value.Length];
            if (!Convert.TryFromBase64String(value, buffer, out var written)) return false;
            cursor = JsonSerializer.Deserialize<SupplierUserCursor>(buffer[..written]);
            return !string.IsNullOrEmpty(cursor.Email);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
