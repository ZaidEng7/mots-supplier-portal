// The back-office list of one supplier's documents.
//
// The one endpoint the written contract specifies in full, and the one that had no implementation at all.
//
//
// NUMBERED PAGES, NOT A CURSOR
//
// The contract's own heading says page mode is the default for back-office lists, and explains why: pages
// suit administrative tables that show a pager with a total, over a finite set.
//
// A supplier's documents are exactly that. Bounded by the document-type catalogue times a handful of
// versions, reviewed in a grid, and a reviewer wants to know how many there are.
//
// The total is counted over the FILTERED set and before the page window, so it is a total rather than the
// size of the page just returned.
//
//
// ORDERING AND TIE-BREAKING
//
// Newest upload first, because the contract's own worked example both sends and echoes that sort. The
// identifier breaks ties, for the same reason every paged query in this codebase carries a tiebreak: two
// documents uploaded in one request share an instant, and skipping rows over a non-deterministic order
// silently repeats and drops rows between pages.
//
//
// THE STATE FILTER
//
// Several states separated by commas, which is the contract's multi-value form and exactly what its own
// worked request sends. An unrecognised member is not silently dropped, because that would make the filter
// narrower than the caller asked for.
//
//
// TWO PLACES THE CONTRACT AND THIS SCHEMA DIVERGE, REPORTED RATHER THAN PAPERED OVER
//
// The contract carries an expiry state beside the document state. This schema has no such column: expiring
// and expired are document states, so the two fields are one thing modelled twice. It is derived, and null
// when the document carries no expiry date at all, because a type that does not track expiry is not
// "valid", it is not in the expiry machine.
//
// The contract also shows a download URL at a path that does not exist here. The real route is emitted
// rather than the documented one, so the divergence is visible instead of hidden behind a plausible string.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class ListSupplierDocumentsPagedHandler(AppDbContext db) : IListSupplierDocumentsPagedHandler
{
    public async Task<ListEnvelope<SupplierDocumentListItemDto>?> HandleAsync(
        string supplierCode, string? state, int page, int? pageSize, CancellationToken ct)
    {
        var supplierId = await db.Suppliers
            .Where(s => s.ReferenceCode == supplierCode)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(ct);
        if (supplierId is null) return null;

        var size = ListEnvelope<SupplierDocumentListItemDto>.ClampPageSize(pageSize);
        var query = db.SupplierDocuments.Where(d => d.SupplierId == supplierId.Value);

        var states = ParseStates(state);
        if (states is { Count: > 0 }) query = query.Where(d => states.Contains(d.State));

        var totalCount = await query.CountAsync(ct);

        var rows = await query
            .OrderByDescending(d => d.UploadedAt).ThenByDescending(d => d.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(d => new
            {
                d.Id,
                TypeCode = db.DocumentTypes.Where(t => t.Id == d.DocumentTypeId).Select(t => t.Code).FirstOrDefault(),
                d.ReferenceCode,
                d.State,
                d.ExpiryDate,
                d.UploadedAt,
            })
            .ToListAsync(ct);

        var data = rows
            .Select(r => new SupplierDocumentListItemDto(
                r.ReferenceCode,
                r.TypeCode ?? string.Empty,
                r.State,
                r.ExpiryDate,
                ExpiryStateOf(r.State, r.ExpiryDate),
                $"/api/v1/documents/{r.ReferenceCode}/content",
                r.UploadedAt))
            .ToList();

        return ListEnvelope<SupplierDocumentListItemDto>.PageOf(
            data, page, size, totalCount, sort: "-uploadedAt", filtersApplied: DescribeFilters(state));
    }

    private static List<DocumentState>? ParseStates(string? state)
    {
        if (string.IsNullOrWhiteSpace(state)) return null;

        var parsed = new List<DocumentState>();
        foreach (var raw in state.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<DocumentState>(raw, ignoreCase: false, out var value)) parsed.Add(value);
        }
        return parsed;
    }

    private static string? ExpiryStateOf(DocumentState state, DateOnly? expiryDate) => state switch
    {
        DocumentState.Expired => "Expired",
        DocumentState.ExpiringSoon => "ExpiringSoon",
        _ => expiryDate is null ? null : "Valid",
    };

    private static IReadOnlyList<string>? DescribeFilters(string? state) =>
        string.IsNullOrWhiteSpace(state) ? null : [$"state={state}"];
}
