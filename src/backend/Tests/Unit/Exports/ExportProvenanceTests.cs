// The provenance block on an export says which filters were absent, not only which were set.
//
// A file that lists only the filters that WERE set is indistinguishable from one whose range was narrower than
// the reader assumes. A missing line reads as a missing filter rather than as an unbounded one.
//
// The control is a filter that was set stating its value, so the absence markers mean absence rather than "this
// block says unbounded for everything".
//
//
// THE RENDERED BLOCK CARRIES THE SAME FACTS WITHOUT THE COMMENT MARKER
//
// Drawn on a right-to-left page, a leading hash is a neutral character that takes the paragraph direction and
// lands at the far right of the line, so a marked line renders with its marker at the end.
//
// That is correct ordering and meaningless output, which is why the page version omits the marker.
//
// And the dates are formatted culture-invariantly, because a date formatted under the server's culture makes the
// same artefact mean different things on different hosts, and an audit file is the last place for that.

namespace MotsSupplierPortal.Tests.Unit.Exports;

using FluentAssertions;
using MotsSupplierPortal.Application.Exports;

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
        var lines = Sample().ToCsvComments("audit export").ToList();

        lines.Should().Contain("# filter.to: (unbounded)");
        lines.Should().Contain("# filter.action: (all)");

        lines.Should().Contain("# filter.from: 2026-01-01T00:00:00.0000000+00:00");
        lines.Should().Contain("# filter.aggregateType: Rfq");
    }

    [Fact]
    public void The_csv_block_is_comment_lines_and_the_page_block_is_not()
    {
        var provenance = Sample();

        provenance.ToCsvComments("audit export").Should().OnlyContain(l => l.StartsWith('#'),
            "spreadsheets import these above the table; an uncommented line would be read as data");

        provenance.ToDisplayLines("audit export").Should().NotContain(l => l.StartsWith('#'));
        provenance.ToDisplayLines("audit export").Should().Contain("scope: all organizations (audit.read)");
    }

    [Fact]
    public void Timestamps_are_round_trip_formatted_regardless_of_the_host_culture()
    {
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
