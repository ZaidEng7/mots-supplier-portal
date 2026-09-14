// The wording of every validation message, in both languages, loaded from the file beside this one.
//
// The sentences live in the JSON file rather than in code because the product owner reviews and
// approves the Arabic, and a reviewer should not have to read code to do it. The file is embedded in
// the built assembly, so there is exactly one copy and it cannot drift from what shipped.
//
// A message is looked up by the property that failed and the rule that failed it. That is the same key
// the file is ordered by and the same key the coverage test compares against the validators, so a
// missing or orphaned sentence is a test failure rather than an English string leaking to a supplier.
//
// Normalize strips collection indexes for the lookup: a failure on the fourth attribute and one on
// the first are the same rule and share one sentence. The index survives in the field path on the
// response, because a client needs to know which input to point at. Only the catalogue key is
// index-free.
//
// The pattern that strips indexes carries a timeout. That is belt and braces rather than a real risk,
// since the pattern is linear and the input is a property name from a validator rather than user
// text, but a pattern without one is a standing invitation for the next pattern here to be written
// the same way and actually misbehave.

namespace MotsSupplierPortal.Api.Errors;

using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;

public static class ValidationCatalogue
{
    public sealed record Entry(string Key, string Code, string Source, string Ar, string En);

    private static readonly FrozenDictionary<string, Entry> Entries = Load();

    public static IReadOnlyCollection<string> Keys => Entries.Keys;

    public static Entry? Find(string? field, string rule) =>
        Entries.TryGetValue($"{Normalize(field)}.{rule}", out var entry) ? entry : null;

    public static string Normalize(string? field) => IndexPattern.Replace(field ?? string.Empty, "[]");

    private static readonly System.Text.RegularExpressions.Regex IndexPattern =
        new(@"\[\d+\]", System.Text.RegularExpressions.RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private static FrozenDictionary<string, Entry> Load()
    {
        var assembly = typeof(ValidationCatalogue).Assembly;
        var name = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith("ValidationCatalogue.jsonc", StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(name)!;
        var entries = JsonSerializer.Deserialize<List<Entry>>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        })!;

        return entries.ToFrozenDictionary(e => e.Key, StringComparer.Ordinal);
    }
}
