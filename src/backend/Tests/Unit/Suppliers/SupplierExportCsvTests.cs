// The supplier export's header and its rows, asserted against each other.
//
// THE COUNT IS THE WHOLE TEST. Something downstream loads this file on a schedule and builds columns from the
// header, so a row with one cell more or fewer than the header silently shifts every column after the mistake:
// tax_id lands under supplier_type, and nothing anywhere reports an error. It is the failure this export can
// actually have, it cannot be seen by reading the code - the header is a list and the row is a hundred lines of
// expressions below it - and it is invisible in a spreadsheet too, because a shifted file still opens.
//
// A supplier with NOTHING attached is tested beside a full one, because that is where the count usually breaks:
// a null child collapses to one empty cell in the writer and to nothing at all in a careless one.
//
// THE FORMATTING ASSERTIONS are about a loader, not a reader. A decimal rendered with a comma under an Arabic
// locale is both a broken number and a broken CSV, since the comma is the field separator. A date rendered
// "21/09/2026" is ambiguous in exactly the way ISO 8601 exists to stop. These are pinned here rather than left
// to the server's culture.
//
// THE EMPTY CELL IS ASSERTED as empty. "null" and "N/A" are values a loader will happily import as text into a
// column that was supposed to be a date.
//
// THE DOCUMENT LISTS ARE POSITIONALLY ALIGNED and it is asserted, because the file's own header comment promises
// it: doc_type_codes[i], doc_states[i] and doc_expiry_dates[i] are one document. A document with no expiry date
// has to hold its place with an empty entry, or every date after it belongs to the wrong document.

namespace MotsSupplierPortal.Tests.Unit.Suppliers;

using FluentAssertions;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Tests.Unit.Domain;
using Xunit;

public sealed class SupplierExportCsvTests
{
    private static SupplierExportRecord Record(
        Supplier supplier,
        IReadOnlyList<SupplierDocument>? documents = null,
        IReadOnlyList<Offering>? offerings = null,
        double? completeness = null) =>
        new(supplier, documents ?? [], offerings ?? [], [], completeness, []);

    [Fact]
    public void A_full_row_has_exactly_as_many_cells_as_the_header_has_columns()
    {
        var cells = SupplierExportCsv.Cells(Record(SupplierTestFactory.Approved()), SupplierExportLookups.Empty);

        cells.Should().HaveCount(
            SupplierExportCsv.Columns.Count,
            "a row one cell out of step with the header shifts every column after it, and nothing downstream "
            + "reports an error - the file still loads, into the wrong columns");
    }

    [Fact]
    public void A_supplier_with_no_children_produces_the_same_number_of_cells()
    {
        var bare = Supplier.Register(
            "SUP-2026-000999", "شركة فارغة", "Empty Co", "CR-0", "Owner", "owner@example.com");

        var cells = SupplierExportCsv.Cells(Record(bare), SupplierExportLookups.Empty);

        cells.Should().HaveCount(SupplierExportCsv.Columns.Count);
    }

    [Fact]
    public void The_header_names_are_unique()
    {
        SupplierExportCsv.Columns.Should().OnlyHaveUniqueItems(
            "a duplicated column name makes the second one unreachable by name in every loader");
    }

    [Fact]
    public void An_absent_value_is_an_empty_cell_and_never_the_word_null()
    {
        var bare = Supplier.Register(
            "SUP-2026-000998", "شركة فارغة", "Empty Co", "CR-0", "Owner", "owner@example.com");

        var cells = SupplierExportCsv.Cells(Record(bare), SupplierExportLookups.Empty);
        var website = cells[At("website")];

        website.Should().BeNull();
        cells.Should().NotContain("null").And.NotContain("N/A");
    }

    [Fact]
    public void Numbers_and_dates_are_invariant_so_the_servers_locale_cannot_change_the_file()
    {
        var supplier = SupplierTestFactory.Approved();
        supplier.AddAddress(AddressKind.Billing, "1 Coast Rd", null, "Latakia", "LAT", "Syria", null, 35.52, 35.79);

        var cells = SupplierExportCsv.Cells(Record(supplier, completeness: 0.75), SupplierExportLookups.Empty);

        cells[At("profile_completeness")].Should().Be("0.75");
        cells[At("created_at")].Should().EndWith("Z");
        cells[At("is_eligible_to_participate")].Should().Be("true");
    }

    [Fact]
    public void A_multi_valued_cell_joins_on_a_semicolon_because_the_comma_is_the_field_separator()
    {
        var supplier = Supplier.Register(
            "SUP-2026-000997", "شركة", "Multi Co", "CR-2", "Owner", "owner@example.com");
        supplier.MarkEmailVerified();
        supplier.LinkCategory("transport", isComplianceCritical: false);
        supplier.LinkCategory("catering", isComplianceCritical: false);

        var cells = SupplierExportCsv.Cells(Record(supplier), SupplierExportLookups.Empty);
        var codes = cells[At("category_codes")];

        codes.Should().Be("catering;transport");
    }

    [Fact]
    public void The_document_lists_line_up_position_by_position_even_when_a_date_is_missing()
    {
        var supplier = SupplierTestFactory.Approved();
        var withExpiry = Document(supplier.Id, new DateOnly(2027, 3, 1));
        var withoutExpiry = Document(supplier.Id, null);

        var cells = SupplierExportCsv.Cells(
            Record(supplier, documents: [withExpiry, withoutExpiry]), SupplierExportLookups.Empty);

        var codes = cells[At("doc_type_codes")]!.Split(';');
        var states = cells[At("doc_states")]!.Split(';');
        var expiries = cells[At("doc_expiry_dates")]!.Split(';');

        codes.Should().HaveCount(2);
        states.Should().HaveCount(2);
        expiries.Should().HaveCount(2, "a document with no expiry has to hold its place, or every date after it "
            + "belongs to the wrong document");
        expiries[1].Should().BeEmpty();
    }

    private static int At(string column) =>
        SupplierExportCsv.Columns.ToList().IndexOf(column);

    private static SupplierDocument Document(Guid supplierId, DateOnly? expiry) =>
        SupplierDocument.CreatePendingScan(
            $"DOC-2026-{Guid.NewGuid().ToString("N")[..6]}",
            supplierId, Guid.CreateVersion7(), 1, "quarantine/key",
            "doc.pdf", "application/pdf", 1024, Guid.CreateVersion7(),
            issueDate: new DateOnly(2026, 1, 1), expiryDate: expiry,
            expiryTracked: expiry is not null, today: new DateOnly(2026, 1, 1));
}
