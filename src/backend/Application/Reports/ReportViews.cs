// Turning each report's figures into the tables an exported file is made of.
//
// ReportText holds the two rules both reports share.
//
// Counts render in Arabic-Indic digits under Arabic. That applies to numbers only. A state's name or a
// reference code is an identifier, and converting its digits produces a string that no longer matches the
// record it names.
//
// A value that could not be measured renders as an explicit marker and never as a zero. No hours elapsed
// and no tender having completed the interval are different facts, and the first is a claim about a fast
// process.
//
// The two views below build one set of tables each: three for the procurement report and two for the
// compliance one.

namespace MotsSupplierPortal.Application.Reports;

using System.Globalization;
using MotsSupplierPortal.Application.Exports;

internal static class ReportText
{
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

    public static string Hours(decimal? value, string locale) =>
        value is null
            ? (locale == "en" ? "(not measured)" : "(غير مقيس)")
            : Digits(value.Value.ToString("0.0", CultureInfo.InvariantCulture), locale);
}

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
                locale == "en" ? "Cycle time" : "زمن الدورة",
                locale == "en"
                    ? ["Interval", "RFQs measured", "Median hours"]
                    : ["الفترة", "عدد الطلبات المقيسة", "الوسيط بالساعات"],
                report.CycleTimes
                    .Select(c => (IReadOnlyList<string>)new[]
                    {
                        c.Key,
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
