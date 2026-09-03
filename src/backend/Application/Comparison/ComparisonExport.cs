using System.Globalization;
using MotsSupplierPortal.Application.Reporting;

namespace MotsSupplierPortal.Application.Comparison;

/// <summary>
/// FR-CMP-005: the comparison matrix as a downloadable artefact. Closes the export deferral EPIC-12
/// flagged rather than dropped.
///
/// <para><b>The two-envelope property is structural and stays structural.</b> This builds its
/// artefact from a <see cref="ComparisonDto"/> that has ALREADY been produced by the screen's own
/// handler. It issues no query of its own, holds no DbContext, and has no access to a proposal's
/// prices except through the nullable members the gate leaves null. So the export cannot become the
/// path that reintroduces what the screen refuses to show - not because it checks the gate a second
/// time, but because there is nothing here to check it with. A second query, however carefully
/// gated, would be a second place for the gate to be wrong.</para>
///
/// <para>Absence is rendered as absence. Where <c>Items</c> or <c>GrandTotal</c> is null the cell is
/// the explicit "not yet visible" marker, never a zero, an empty string, or a dash that a reader
/// could mistake for a submitted price of nothing.</para>
/// </summary>
public static class ComparisonExport
{
    /// <summary>
    /// The best-value marker, as an icon AND a word.
    ///
    /// <para>ACCESSIBILITY.md 1.4.1: colour is never the only carrier of meaning. On a screen that
    /// rule is usually met with a badge; in a PDF it is easier to get wrong, because shading a
    /// column is the obvious way to mark a winner and it is invisible to anyone who prints in
    /// greyscale, has low vision, or has the file read to them. The rank column carries the star and
    /// the word together, and nothing in this export is distinguished by colour at all.</para>
    /// </summary>
    public const string BestValueMarker = "★";

    private static string BestValueLabel(string locale) => locale == "en" ? "Best value" : "أفضل قيمة";

    /// <summary>The marker for a value the two-envelope gate has not opened yet.</summary>
    private static string NotVisible(string locale) => locale == "en" ? "(not yet visible)" : "(غير متاح بعد)";

    /// <summary>
    /// A proposal with no rank. An em dash in both languages - it is a typographic mark, not a word,
    /// so there is nothing here to translate.
    ///
    /// <para>This was written as a locale ternary returning the same string on both branches, which
    /// Sonar correctly reports as a BUG (S3923) rather than a style problem: a conditional whose
    /// branches are identical is either a copy-paste error or a translation someone forgot to
    /// finish, and there is no way to tell which by reading it. Here it was the latter shape without
    /// the intent - so the constant says so instead of a condition implying a difference that does
    /// not exist.</para>
    /// </summary>
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
            // The evaluation state is the reason a column is empty. Without it in the artefact, a
            // comparison exported before consolidation is indistinguishable from one where every
            // supplier submitted no prices.
            new ExportFilterValue("evaluationState", comparison.EvaluationState),
        ]);

    /// <summary>Column headers, then one row per proposal, in the export's language.</summary>
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
        // Icon AND text, never one without the other.
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

    /// <summary>
    /// R-1: counts, quantities, currency and scores render in Arabic-Indic digits under Arabic.
    ///
    /// <para>Applied to NUMBERS only. A reference code is an identifier, not a quantity, and
    /// transliterating its digits would produce a string that does not match the record it names -
    /// which is the same rule the SPA follows.</para>
    /// </summary>
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
