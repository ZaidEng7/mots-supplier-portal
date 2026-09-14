// The paging cursor for a supplier's own team list, ordered by email address.
//
// It is its own type rather than the shared time-ordered one, and it encodes differently: as JSON rather than
// two values joined by a colon.
//
// An email address is arbitrary text and cannot safely share a fixed separator the way a timestamp can. A
// colon in an address would split the cursor in the wrong place.

namespace MotsSupplierPortal.Application.Suppliers;

using System.Text.Json;

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
