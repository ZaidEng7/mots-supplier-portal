using FluentAssertions;
using MotsSupplierPortal.Application.Audit;

namespace MotsSupplierPortal.Tests.Unit.Audit;

/// <summary>MSP-75/FR-AUD-004: the CSV escaping rules in isolation from the endpoint/DB, per RFC
/// 4180 - a field is quoted only when it needs to be, and an embedded quote is doubled rather than
/// escaped with a backslash (CSV has no backslash-escape convention).</summary>
public sealed class AuditCsvRowTests
{
    private static AuditLogEntryDto Entry(
        string? actorLabel = null, string? action = "supplier.approve",
        string? fromState = "Pending", string? toState = "Approved") =>
        new(Guid.Parse("00000000-0000-0000-0000-000000000001"),
            new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero),
            "Supplier", Guid.Parse("00000000-0000-0000-0000-000000000002"),
            action!, fromState, toState, actorLabel);

    [Fact]
    public void A_plain_row_is_not_quoted()
    {
        var row = AuditCsvRow.Format(Entry(actorLabel: "Jane Reviewer"));

        row.Should().Be("00000000-0000-0000-0000-000000000001,2026-08-30T12:00:00.0000000+00:00,"
            + "Supplier,00000000-0000-0000-0000-000000000002,supplier.approve,Pending,Approved,Jane Reviewer");
    }

    [Fact]
    public void A_field_containing_a_comma_is_quoted()
    {
        var row = AuditCsvRow.Format(Entry(actorLabel: "Reviewer, Senior"));

        row.Should().Contain("\"Reviewer, Senior\"",
            "an unquoted comma inside a field would be read as an extra column by any CSV reader");
    }

    [Fact]
    public void An_embedded_quote_is_doubled_and_the_field_quoted()
    {
        var row = AuditCsvRow.Format(Entry(actorLabel: "The \"Boss\""));

        row.Should().Contain("\"The \"\"Boss\"\"\"",
            "RFC 4180 doubles an embedded quote rather than backslash-escaping it");
    }

    [Fact]
    public void A_null_field_becomes_an_empty_column_not_the_literal_word_null()
    {
        var row = AuditCsvRow.Format(Entry(actorLabel: null, fromState: null, toState: null));

        row.Should().EndWith(",,,", "a system actor with no state transition has no FromState, " +
            "ToState, or ActorLabel to show, and empty columns say that honestly")
            .And.NotContain("null");
    }
}
