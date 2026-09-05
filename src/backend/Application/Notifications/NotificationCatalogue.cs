using System.Collections.Frozen;
using System.Text.Json;

namespace MotsSupplierPortal.Application.Notifications;

/// <summary>
/// The copy catalogue, loaded from the embedded <c>NotificationCatalogue.jsonc</c>.
///
/// <para>Same shape as §7.2's validation catalogue and for the same reason: the product owner
/// reviews and approves the Arabic, and a reviewer should not have to read C# to do it. Embedded, so
/// there is exactly one copy and it cannot drift from what shipped.</para>
/// </summary>
public static partial class NotificationCatalogue
{
    public sealed record Entry(string Type, string Source, string TitleAr, string TitleEn, string BodyAr, string BodyEn);

    private static readonly FrozenDictionary<string, Entry> Entries = Load();

    public static IReadOnlyCollection<string> Types => Entries.Keys;

    public static Entry For(string type) =>
        Entries.TryGetValue(type, out var entry)
            ? entry
            : throw new InvalidOperationException(
                $"No copy for notification type '{type}'. Add it to NotificationCatalogue.jsonc - " +
                "a notification with no authored words would reach a supplier as an empty row.");

    /// <summary>
    /// Interpolates the payload's public codes into the copy. Only tokens the copy actually names
    /// are replaced, and only from values the payload allow-list already permits, so this cannot
    /// become a second route for data into a message body.
    /// </summary>
    public static (string TitleAr, string TitleEn, string BodyAr, string BodyEn) Render(
        string type, IReadOnlyDictionary<string, string?> tokens) =>
        Render(For(type), tokens);

    /// <summary>Renders copy that is not the shipped entry - an administrator's override (T-061).
    /// The interpolation is identical, deliberately: an override must not gain a capability the
    /// shipped copy does not have.</summary>
    public static (string TitleAr, string TitleEn, string BodyAr, string BodyEn) Render(
        Entry entry, IReadOnlyDictionary<string, string?> tokens)
    {
        string Fill(string text) => tokens.Aggregate(text, (current, token) =>
            token.Value is null ? current : current.Replace($"{{{token.Key}}}", token.Value, StringComparison.Ordinal));

        return (Fill(entry.TitleAr), Fill(entry.TitleEn), Fill(entry.BodyAr), Fill(entry.BodyEn));
    }

    /// <summary>
    /// The tokens a type's SHIPPED copy names, in any of its four texts.
    ///
    /// <para>This is the permitted set for an override (T-061/D-34). It is derived rather than
    /// declared because it is already exact: the payload for a type carries the values its shipped
    /// copy interpolates, so a token outside this set has no value to fill it and would reach the
    /// recipient as the literal text <c>{price}</c>.</para>
    /// </summary>
    public static IReadOnlySet<string> TokensFor(string type)
    {
        var entry = For(type);
        var tokens = new HashSet<string>(StringComparer.Ordinal);

        foreach (var text in new[] { entry.TitleAr, entry.TitleEn, entry.BodyAr, entry.BodyEn })
        {
            foreach (var token in TokenPattern().Matches(text).Select(m => m.Groups[1].Value))
            {
                tokens.Add(token);
            }
        }

        return tokens;
    }

    /// <summary>Tokens named in <paramref name="text"/>, whatever they are.</summary>
    public static IReadOnlySet<string> TokensIn(string text) =>
        TokenPattern().Matches(text).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    [System.Text.RegularExpressions.GeneratedRegex(@"\{([A-Za-z][A-Za-z0-9_]*)\}")]
    private static partial System.Text.RegularExpressions.Regex TokenPattern();

    private static FrozenDictionary<string, Entry> Load()
    {
        var assembly = typeof(NotificationCatalogue).Assembly;
        var name = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith("NotificationCatalogue.jsonc", StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(name)!;
        var entries = JsonSerializer.Deserialize<List<Entry>>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        })!;

        return entries.ToFrozenDictionary(e => e.Type, StringComparer.Ordinal);
    }
}
