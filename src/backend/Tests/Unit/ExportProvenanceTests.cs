using FluentAssertions;
using MotsSupplierPortal.Application.Reporting;

namespace MotsSupplierPortal.Tests.Unit;

public sealed class ExportProvenanceTests
{
    private static ExportProvenance Sample() => new(
        new DateTimeOffset(2026, 9, 3, 18, 0, 0, TimeSpan.Zero),
        "all organizations (audit.read)",
        [
            ExportFilterValue.Optional("aggregateType", "Rfq"),
            ExportFilterValue.Optional("action", null),
            ExportFilterValue.Bound("from", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            ExportFilterValue.Bound("to", null),
        ]);

    [Fact]
    public void An_absent_filter_is_stated_rather_than_omitted()
    {
        // The point of the block. A file that lists only the filters that were SET is
        // indistinguishable from one whose range was narrower than the reader assumes - and a
        // missing line reads as a missing filter, not as an unbounded one.
        var lines = Sample().ToCsvComments("audit export").ToList();

        lines.Should().Contain("# filter.to: (unbounded)");
        lines.Should().Contain("# filter.action: (all)");

        // Control: a filter that WAS set states its value, so the markers above mean absence and
        // not "this block says (unbounded) for everything".
        lines.Should().Contain("# filter.from: 2026-01-01T00:00:00.0000000+00:00");
        lines.Should().Contain("# filter.aggregateType: Rfq");
    }

    [Fact]
    public void The_csv_block_is_comment_lines_and_the_page_block_is_not()
    {
        var provenance = Sample();

        provenance.ToCsvComments("audit export").Should().OnlyContain(l => l.StartsWith('#'),
            "spreadsheets import these above the table; an uncommented line would be read as data");

        // The rendered block carries the same facts without the marker. Drawn on an RTL page a
        // leading '#' is a neutral character that takes the paragraph direction and lands at the far
        // right of the line - "# scope: X" renders as "scope: X #". Correct bidi, meaningless output.
        provenance.ToDisplayLines("audit export").Should().NotContain(l => l.StartsWith('#'));
        provenance.ToDisplayLines("audit export").Should().Contain("scope: all organizations (audit.read)");
    }

    [Fact]
    public void Timestamps_are_round_trip_formatted_regardless_of_the_host_culture()
    {
        // The §12.5 bug: a date formatted under the server's culture makes the same artefact mean
        // different things on different hosts, and an audit file is the last place for that.
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("ar-SY");
            Sample().ToCsvComments("x").Should().Contain("# generated: 2026-09-03T18:00:00.0000000+00:00");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }
}
