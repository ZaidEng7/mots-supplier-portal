using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Notifications;

/// <summary>
/// The keyset cursor. Opaque to the client on purpose - a cursor that reads as a timestamp invites
/// callers to construct one, and then its format is a contract.
/// </summary>
internal static class NotificationCursor
{
    public static string Encode(DateTimeOffset createdAt, Guid id) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{createdAt.UtcTicks}|{id}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool TryDecode(string? cursor, out DateTimeOffset createdAt, out Guid id)
    {
        createdAt = default;
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(cursor)) return false;

        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
            var parts = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded)).Split('|');

            if (parts.Length != 2 || !long.TryParse(parts[0], out var ticks) || !Guid.TryParse(parts[1], out id)) return false;

            createdAt = new DateTimeOffset(ticks, TimeSpan.Zero);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
