// Building a list-read URL for the ERP, where two different encodings meet in one string.
//
// THE PATH AND THE QUERY ARE ENCODED DIFFERENTLY AND SWAPPING THEM FAILS IN TWO DIFFERENT WAYS. A record type
// with a space belongs in the path percent-encoded - "Dynamic%20Link" - and the same name encoded as a query
// value becomes "Dynamic+Link", which the ERP answers 404. A JSON filter encoded as if it were a path segment
// keeps its brackets and quotes, and the ERP answers 417 DataError. Both are one-character mistakes with
// different symptoms, so both are pinned.
//
// "Dynamic Link" IS NOT AN ARBITRARY EXAMPLE. It is the record type that joins a supplier to its address and
// contact, it is the one this product will need next, and it is the one with a space in it.
//
// THE MISSING FIELD LIST THROWS, and that is the interesting case rather than an argument check for its own
// sake: without a field list the ERP returns an array of the right length with nothing in it but identifiers. The
// request succeeds. A caller who forgets gets a clean-looking answer to a question they did not ask, so the only
// safe place to catch it is before the call.

namespace MotsSupplierPortal.Tests.Unit.Erp;

using FluentAssertions;
using MotsSupplierPortal.Infrastructure.Integration.Erp;

public sealed class ErpQueryTests
{
    private static readonly string[] TwoFields = ["name", "supplier_name"];

    [Fact]
    public void A_record_type_with_a_space_is_percent_encoded_in_the_path()
    {
        var url = ErpQuery.List("Dynamic Link", TwoFields);

        url.Should().StartWith("api/resource/Dynamic%20Link?",
            "a space encoded as '+' belongs to query values and answers 404 in a path");
        url.Should().NotContain("Dynamic+Link");
    }

    [Fact]
    public void The_field_list_travels_as_one_url_encoded_json_document()
    {
        var url = ErpQuery.List("Supplier", TwoFields);

        url.Should().Contain("fields=%5B%22name%22%2C%22supplier_name%22%5D",
            "the brackets and quotes must be encoded, or the ERP answers 417 DataError");
        url.Should().NotContain("[\"name\"");
    }

    [Fact]
    public void Filters_are_only_sent_when_there_are_some()
    {
        ErpQuery.List("Supplier", TwoFields).Should().NotContain("filters=");

        ErpQuery.List("Supplier", TwoFields, [["company", "=", "Seven Gates"]])
            .Should().Contain("filters=%5B%5B%22company%22%2C%22%3D%22%2C%22Seven%20Gates%22%5D%5D");
    }

    [Fact]
    public void The_whole_list_is_asked_for_by_default()
    {
        ErpQuery.List("Supplier", TwoFields).Should().Contain("limit_page_length=0",
            "the supplier list is about eighty rows, which the ERP's own guidance calls a small master list");
    }

    [Fact]
    public void A_page_length_is_carried_when_one_is_given()
    {
        ErpQuery.List("Supplier", TwoFields, limitPageLength: 20).Should().Contain("limit_page_length=20");
    }

    [Fact]
    public void An_order_is_carried_and_encoded()
    {
        ErpQuery.List("Supplier", TwoFields, orderBy: "name asc").Should().Contain("order_by=name%20asc");
    }

    [Fact]
    public void A_read_with_no_field_list_is_refused_before_it_is_sent()
    {
        var act = () => ErpQuery.List("Supplier", []);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*only the record name*",
                "the ERP answers such a request successfully, with an array of empty records");
    }
}
