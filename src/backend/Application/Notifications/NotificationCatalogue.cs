// The wording of every notification, in both languages, loaded from the file beside this one.
//
// Same arrangement as the validation messages and for the same reason: the product owner reviews and approves
// the Arabic, and a reviewer should not have to read code to do it. It is embedded in the built assembly, so
// there is exactly one copy and it cannot drift from what shipped.
//
// Rendering fills the placeholders the wording actually names, and only from values the payload's own
// allow-list already permits. So this cannot become a second route for data into a message body.
//
// An administrator's override is rendered through the identical path, deliberately. An override must not gain
// a capability the shipped wording does not have.

namespace MotsSupplierPortal.Application.Notifications;

using System.Collections.Frozen;
using System.Text.Json;

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

    public static (string TitleAr, string TitleEn, string BodyAr, string BodyEn) Render(
        string type, IReadOnlyDictionary<string, string?> tokens) =>
        Render(For(type), tokens);

    public static (string TitleAr, string TitleEn, string BodyAr, string BodyEn) Render(
        Entry entry, IReadOnlyDictionary<string, string?> tokens)
    {
        string Fill(string text) => tokens.Aggregate(text, (current, token) =>
            token.Value is null ? current : current.Replace($"{{{token.Key}}}", token.Value, StringComparison.Ordinal));

        return (Fill(entry.TitleAr), Fill(entry.TitleEn), Fill(entry.BodyAr), Fill(entry.BodyEn));
    }

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
