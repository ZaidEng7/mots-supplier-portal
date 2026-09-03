using System.Globalization;
using MotsSupplierPortal.Application.Reporting;

namespace MotsSupplierPortal.Application.Reports;

/// <summary>
/// Shared rendering rules for both report artefacts.
/// </summary>
internal static class ReportText
{
    /// <summary>
    /// R-1: counts render in Arabic-Indic digits under Arabic. Applied to NUMBERS only - a state key
    /// or a reference code is an identifier, and transliterating its digits produces a string that
    /// no longer matches the record it names.
    /// </summary>
    public static string Digits(string value, string locale)
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

    public static string Count(int value, string locale) =>
        Digits(value.ToString(CultureInfo.InvariantCulture), locale);

    /// <summary>
    /// A measured value, or the explicit marker for one that could not be measured.
    ///
    /// <para>Never a zero. A cycle time of "0.0 hours" and "no RFQ has completed this interval" are
    /// different facts, and the first is a claim about a fast process.</para>
    /// </summary>
    public static string Hours(decimal? value, string locale) =>
        value is null
            ? (locale == "en" ? "(not measured)" : "(غير مقيس)")
            : Digits(value.Value.ToString("0.0", CultureInfo.InvariantCulture), locale);
}

/// <summary>FEAT-19.1's artefact: three tables and their headings.</summary>
public static class ProcurementReportView
{
    public static string Title(string locale) => locale == "en" ? "Procurement report" : "تقرير المشتريات";

    public static string ArtefactName(string locale) => locale == "en" ? "procurement report" : "تقرير المشتريات";

    public static IReadOnlyList<ReportSection> Sections(ProcurementReportDto report, string locale)
    {
        var countColumns = locale == "en" ? new[] { "State", "Count" } : ["الحالة", "العدد"];

        return
        [
            new ReportSection(
                locale == "en" ? "RFQs by state" : "طلبات عروض الأسعار حسب الحالة",
                countColumns,
                report.RfqsByState
                    .Select(c => (IReadOnlyList<string>)new[] { c.Key, ReportText.Count(c.Count, locale) })
                    .ToList()),

            new ReportSection(
                locale == "en" ? "Cycle time (median hours)" : "زمن الدورة (الوسيط بالساعات)",
                locale == "en"
                    ? ["Interval", "RFQs measured", "Median hours"]
                    : ["الفترة", "عدد الطلبات المقيسة", "الوسيط بالساعات"],
                report.CycleTimes
                    .Select(c => (IReadOnlyList<string>)new[]
                    {
                        c.Key,
                        // The sample size travels with the median. A median over two RFQs and one
                        // over two hundred are different claims, and a table showing only the number
                        // invites the second reading.
                        ReportText.Count(c.SampleSize, locale),
                        ReportText.Hours(c.MedianHours, locale),
                    })
                    .ToList()),

            new ReportSection(
                locale == "en" ? "Awards by state" : "الترسيات حسب الحالة",
                countColumns,
                report.AwardsByState
                    .Select(c => (IReadOnlyList<string>)new[] { c.Key, ReportText.Count(c.Count, locale) })
                    .ToList()),
        ];
    }
}

/// <summary>FEAT-19.2's artefact.</summary>
public static class ComplianceReportView
{
    public static string Title(string locale) => locale == "en" ? "Compliance report" : "تقرير الامتثال";

    public static string ArtefactName(string locale) => locale == "en" ? "compliance report" : "تقرير الامتثال";

    public static IReadOnlyList<ReportSection> Sections(ComplianceReportDto report, string locale)
    {
        var countColumns = locale == "en" ? new[] { "State", "Count" } : ["الحالة", "العدد"];

        return
        [
            new ReportSection(
                locale == "en" ? "Suppliers by lifecycle state" : "الموردون حسب حالة دورة الحياة",
                countColumns,
                report.SuppliersByLifecycleState
                    .Select(c => (IReadOnlyList<string>)new[] { c.Key, ReportText.Count(c.Count, locale) })
                    .ToList()),

            new ReportSection(
                locale == "en" ? "Documents by state (latest versions)" : "المستندات حسب الحالة (أحدث الإصدارات)",
                countColumns,
                report.DocumentsByState
                    .Select(c => (IReadOnlyList<string>)new[] { c.Key, ReportText.Count(c.Count, locale) })
                    .ToList()),
        ];
    }
}
