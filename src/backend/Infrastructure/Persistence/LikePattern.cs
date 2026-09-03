namespace MotsSupplierPortal.Infrastructure.Persistence;

/// <summary>
/// Turns caller-supplied text into a literal inside a SQL LIKE/ILIKE pattern.
///
/// <para>Interpolating a search term straight into <c>$"%{term}%"</c> makes the caller's <c>%</c> and
/// <c>_</c> pattern syntax rather than characters: <c>%</c> matches everything and <c>a_c</c>
/// matches "abc". The value is still a parameter, so this is not SQL injection - it is the narrower
/// problem that the caller's string stops meaning what it says.</para>
///
/// <para>The escape character is backslash, declared explicitly in the ESCAPE clause rather than
/// left to the server's default. PostgreSQL's default already is backslash, but that is a setting
/// (<c>standard_conforming_strings</c> and the pattern's own dialect) rather than a guarantee, and a
/// pattern whose meaning depends on server configuration is not worth the two characters saved.</para>
/// </summary>
public static class LikePattern
{
    public const string EscapeCharacter = "\\";

    /// <summary>
    /// Escapes the escape character FIRST - otherwise escaping <c>%</c> to <c>\%</c> would then have
    /// its own backslash escaped again, and a search for a literal backslash would break.
    /// </summary>
    public static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);
}
