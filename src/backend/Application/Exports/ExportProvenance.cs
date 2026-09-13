// What an exported file says about itself: when it was produced, whose data it covers, and every filter
// that shaped it.
//
// It belongs to the export engine rather than to any one export. Audit rows, comparison sheets and
// report tables are all records of a tender, and once a file is detached from the request that produced
// it, one covering a truncated range is indistinguishable from a complete one. That is exactly the state
// it is in when somebody attaches it to a dispute.
//
// Stating the filters inside the file turns "this is everything between these dates" into a claim the
// file makes and a reader can check.
//
// The scope is written in words, such as every organization, or one supplier's own trail. A file that
// does not say this cannot be checked against what its reader was entitled to see.
//
// Every filter the route accepts is listed, present or not. An absent one is rendered as unbounded or as
// all, rather than omitted, because a missing line reads as a missing filter and that is the exact
// ambiguity this exists to remove.

namespace MotsSupplierPortal.Application.Exports;

using System.Globalization;

public sealed record ExportProvenance(
    DateTimeOffset GeneratedAt,
    string Scope,
    IReadOnlyList<ExportFilterValue> Filters)
{
    public const string Product = "MOTS Supplier Portal";

    public IEnumerable<string> ToCsvComments(string artefactName)
    {
        yield return $"# {Product} - {artefactName}";
        yield return $"# generated: {GeneratedAt.ToString("O", CultureInfo.InvariantCulture)}";
        yield return $"# scope: {Scope}";

        foreach (var filter in Filters)
        {
            yield return $"# filter.{filter.Key}: {filter.Display}";
        }
    }

    public IEnumerable<string> ToDisplayLines(string artefactName)
    {
        yield return $"{Product} — {artefactName}";
        yield return $"generated: {GeneratedAt.ToString("O", CultureInfo.InvariantCulture)}";
        yield return $"scope: {Scope}";

        foreach (var filter in Filters)
        {
            yield return $"{filter.Key}: {filter.Display}";
        }
    }
}

public sealed record ExportFilterValue(string Key, string Display)
{
    public static ExportFilterValue Optional(string key, string? value) =>
        new(key, string.IsNullOrWhiteSpace(value) ? "(all)" : value);

    public static ExportFilterValue Bound(string key, DateTimeOffset? value) =>
        new(key, value?.ToString("O", CultureInfo.InvariantCulture) ?? "(unbounded)");

    public static ExportFilterValue OptionalId(string key, Guid? value) =>
        new(key, value?.ToString() ?? "(all)");
}
