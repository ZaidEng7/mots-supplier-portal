// Turning caller-supplied text into a literal inside a pattern match.
//
// Interpolating a search term straight into a pattern makes the caller's wildcard characters syntax rather than
// characters: one matches everything, and a single-character wildcard in the middle of a word matches neighbours.
//
// The value is still a parameter, so this is not an injection. It is the narrower problem that the caller's string
// stops meaning what it says.
//
// The escape character is declared explicitly rather than left to the server's default. The default already is the
// same character, but that is a setting rather than a guarantee, and a pattern whose meaning depends on server
// configuration is not worth the two characters saved.
//
// The escape character itself is escaped FIRST, because otherwise escaping a wildcard would then have its own
// escape escaped again, and a search for a literal one would break.

namespace MotsSupplierPortal.Infrastructure.Persistence;

public static class LikePattern
{
    public const string EscapeCharacter = "\\";

    public static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);
}
