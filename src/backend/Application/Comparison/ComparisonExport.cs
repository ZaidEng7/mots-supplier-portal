// The comparison matrix as a downloadable file.
//
//
// WHY THE EXPORT CANNOT LEAK WHAT THE SCREEN HIDES
//
// This builds its file from a comparison the screen's own handler has already produced. It issues no query
// of its own, holds no database context, and has no way to reach a bid's prices except through the
// optional fields the gate leaves empty.
//
// So the export cannot become the path that reintroduces what the screen refuses to show. Not because it
// checks the gate a second time, but because there is nothing here to check it with. A second query,
// however carefully gated, would be a second place for the gate to be wrong.
//
// Absence is rendered as absence. Where a total is empty the cell carries an explicit not-yet-visible
// marker, never a zero, an empty cell, or a dash a reader could mistake for a submitted price of nothing.
//
//
// THE BEST-VALUE MARKER IS A SYMBOL AND A WORD
//
// Colour is never the only carrier of meaning. On a screen that rule is usually met with a badge; in a
// printed file it is easier to get wrong, because shading the winning column is the obvious way to mark it
// and that is invisible to anybody printing in grey, reading with low vision, or having the file read
// aloud.
//
// The rank column carries the symbol and the word together, and nothing in this export is distinguished by
// colour at all.

namespace MotsSupplierPortal.Application.Comparison;

using System.Globalization;
using MotsSupplierPortal.Application.Exports;

public static class ComparisonExport
{
    public const string BestValueMarker = "★";

    private static string BestValueLabel(string locale) => locale == "en" ? "Best value" : "أفضل قيمة";

    private static string NotVisible(string locale) => locale == "en" ? "(not yet visible)" : "(غير متاح بعد)";

    private const string NotRanked = "—";

    public static string Title(ComparisonDto comparison, string locale) =>
        locale == "en"
            ? $"Proposal comparison — {comparison.RfqReferenceCode}"
            : $"مقارنة العروض — {comparison.RfqReferenceCode}";

    public static string ArtefactName(string locale) => locale == "en" ? "comparison export" : "تصدير المقارنة";

    public static ExportProvenance Provenance(ComparisonDto comparison, DateTimeOffset generatedAt, string scope) =>
        new(generatedAt, scope,
        [
            new ExportFilterValue("rfq", comparison.RfqReferenceCode),
            new ExportFilterValue("evaluationState", comparison.EvaluationState),
        ]);

    public static IReadOnlyList<string> Columns(string locale) => locale == "en"
        ? ["Supplier", "Proposal", "Submitted", "Rank", "Total", "Weighted score"]
        : ["المورّد", "العرض", "تاريخ التقديم", "الترتيب", "الإجمالي", "الدرجة الموزونة"];

    public static IReadOnlyList<IReadOnlyList<string>> Rows(ComparisonDto comparison, string locale) =>
        comparison.Proposals.Select(proposal => (IReadOnlyList<string>)new[]
        {
            locale == "en" ? proposal.SupplierDisplayNameEn : proposal.SupplierDisplayNameAr,
            proposal.ProposalReferenceCode,
            proposal.SubmittedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Rank(proposal, locale),
            Money(proposal.GrandTotal, proposal.CurrencyCode, locale),
            Score(proposal.WeightedTotal, locale),
        }).ToList();

    private static string Rank(ComparisonProposalDto proposal, string locale) => proposal.Rank switch
    {
        1 => $"{BestValueMarker} 1 — {BestValueLabel(locale)}",
        { } rank => Digits(rank.ToString(CultureInfo.InvariantCulture), locale),
        null => NotRanked,
    };

    private static string Money(decimal? amount, string? currencyCode, string locale)
    {
        if (amount is null) return NotVisible(locale);

        var formatted = amount.Value.ToString("N2", CultureInfo.InvariantCulture);
        var withDigits = Digits(formatted, locale);
        return currencyCode is null ? withDigits : $"{withDigits} {currencyCode}";
    }

    private static string Score(decimal? score, string locale) =>
        score is null ? NotVisible(locale) : Digits(score.Value.ToString("N2", CultureInfo.InvariantCulture), locale);

    private static string Digits(string value, string locale)
    {
        if (locale == "en") return value;

        return string.Create(value.Length, value, (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = source[i] is >= '0' and <= '9' ? (char)('٠' + (source[i] - '0')) : source[i];
            }
        });
    }
}
