// Building the URL for one list read against the ERP.
//
// TWO KINDS OF ENCODING, AND MIXING THEM UP IS THE WHOLE POINT OF THIS FILE. The record type sits in the PATH
// and record types contain spaces - "Sales Invoice", "Dynamic Link" - so the segment is percent-encoded. The
// fields and filters sit in the QUERY and are JSON documents, so the whole document is URL-encoded as one value.
// Encoding a JSON filter the way one encodes a path segment leaves the brackets and quotes intact and the ERP
// answers 417 DataError; encoding a path segment the way one encodes a query value turns a space into a plus and
// the ERP answers 404.
//
// WITHOUT A FIELD LIST ONLY THE NAME COMES BACK. That is the ERP's documented default and it is a silent one:
// the request succeeds, the array has the right number of entries, and every entry is empty but for an
// identifier. A caller who forgets the list gets a clean-looking answer to a question they did not ask, so the
// list is a required argument rather than an optional convenience.
//
// AN UNKNOWN FIELD NAME IS REFUSED RATHER THAN IGNORED - 417 DataError - which is the behaviour worth having.
// It means a field removed from the ERP's schema breaks the read loudly instead of quietly arriving null
// forever.
//
// limit_page_length=0 MEANS EVERYTHING and the ERP's own documentation restricts it to small master lists. The
// supplier list is about eighty rows, so it qualifies, and paging eighty rows would be machinery with no
// purpose. That judgement is recorded here because it stops being true if the list grows.

namespace MotsSupplierPortal.Infrastructure.Integration.Erp;

using System.Text.Json;

public static class ErpQuery
{
    public const int EverythingPageLength = 0;

    public static string List(
        string doctype,
        IReadOnlyList<string> fields,
        IReadOnlyList<IReadOnlyList<object>>? filters = null,
        int limitPageLength = EverythingPageLength,
        string? orderBy = null)
    {
        if (fields.Count == 0)
        {
            throw new ArgumentException(
                "A field list is required: without one the ERP returns only the record name.", nameof(fields));
        }

        var query = new List<string>
        {
            $"fields={Uri.EscapeDataString(JsonSerializer.Serialize(fields))}",
            $"limit_page_length={limitPageLength}",
        };

        if (filters is { Count: > 0 })
        {
            query.Add($"filters={Uri.EscapeDataString(JsonSerializer.Serialize(filters))}");
        }

        if (orderBy is not null)
        {
            query.Add($"order_by={Uri.EscapeDataString(orderBy)}");
        }

        return $"api/resource/{Uri.EscapeDataString(doctype)}?{string.Join('&', query)}";
    }
}
