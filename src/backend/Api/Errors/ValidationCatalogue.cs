using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;

namespace MotsSupplierPortal.Api.Errors;

/// <summary>
/// The §7.2 message catalogue, loaded from <c>ValidationCatalogue.jsonc</c>.
///
/// <para>The strings live in the .jsonc file rather than in C# because the product owner reviews and
/// approves the Arabic, and a reviewer should not have to read code to do that. The file is embedded
/// in the assembly, so there is exactly one copy and it cannot drift from what shipped.</para>
///
/// <para>Lookup is by <c>"{PropertyName}.{RuleName}"</c> - the same key the catalogue is ordered by
/// and the same key <c>ValidationCatalogueCoverageTests</c> compares against the validators, so a
/// missing or orphaned entry is a test failure rather than an English string leaking to a supplier.</para>
/// </summary>
public static class ValidationCatalogue
{
    public sealed record Entry(string Key, string Code, string Source, string Ar, string En);

    private static readonly FrozenDictionary<string, Entry> Entries = Load();

    public static IReadOnlyCollection<string> Keys => Entries.Keys;

    public static Entry? Find(string? field, string rule) =>
        Entries.TryGetValue($"{Normalize(field)}.{rule}", out var entry) ? entry : null;

    /// <summary>
    /// Collection indices are stripped for lookup: a failure on <c>Attributes[3].Key</c> and one on
    /// <c>Attributes[0].Key</c> are the same rule and share one sentence. The index survives in the
    /// emitted <c>field</c> path - §7.2 needs <c>items[0].unitPrice</c> to point at a specific input -
    /// it is only the catalogue key that is index-free.
    /// </summary>
    public static string Normalize(string? field) => IndexPattern.Replace(field ?? string.Empty, "[]");

    // The timeout is belt-and-braces rather than a real risk - the pattern is linear and the input is
    // a property name from a validator, not user text - but a regex without one is a standing
    // invitation for the next pattern here to be written the same way and actually backtrack.
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
