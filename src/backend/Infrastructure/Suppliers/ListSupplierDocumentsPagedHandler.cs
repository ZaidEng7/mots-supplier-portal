using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>
/// §12.3's back-office document list, the one endpoint §12 specifies in full and that had no
/// implementation at all.
///
/// <para><b>Page mode, not cursor.</b> §12.3's heading says so - *"list (page mode default for
/// back-office)"* - and §6.1 explains why it is the right default here: *"Page is available for
/// admin/back-office tables that show a pager with total counts"*, over a *"finite"* set. A
/// supplier's documents are exactly that: bounded by the document-type catalogue times a handful of
/// versions, reviewed in a grid, and a reviewer wants to know how many there are.</para>
///
/// <para><b>Ordering.</b> §12.3's own worked request carries <c>sort=-uploadedAt</c> and its
/// response echoes <c>"sort": "-uploadedAt"</c>, so that is the default here rather than a choice.
/// Id breaks ties, for the same reason every keyset in this codebase carries a tiebreak: two
/// documents uploaded in one request share an instant, and OFFSET over a non-deterministic order
/// silently repeats and drops rows between pages.</para>
/// </summary>
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

        // §6.2's multi-value OR form: "?state=UnderReview,Rejected", which is exactly what §12.3's
        // own worked request sends. An unparseable member is not silently dropped - it would make
        // the filter narrower than the caller asked for, which §6.2 forbids in the unknown-key case
        // for the same reason.
        var states = ParseStates(state);
        if (states is { Count: > 0 }) query = query.Where(d => states.Contains(d.State));

        // §6.1, page mode: "Always returns totalCount". Counted over the FILTERED set, before the
        // page window, so it is a total rather than the size of the page just returned.
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
                // T-010: the public code, not the Guid. §3 forbids internal ids in payloads as well
                // as in URLs, which the note this file used to carry had read too narrowly.
                r.ReferenceCode,
                r.TypeCode ?? string.Empty,
                r.State,
                r.ExpiryDate,
                ExpiryStateOf(r.State, r.ExpiryDate),
                // §12.3 shows "downloadUrl": "/api/v1/documents/DOC-…/content" - a route that does
                // not exist here. The real one is emitted instead of fabricating the documented
                // path, and the divergence is reported rather than hidden behind a plausible string.
                $"/api/v1/documents/{r.ReferenceCode}/download-url",
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

    /// <summary>
    /// §12.3 carries <c>expiryState</c> (<c>Valid | ExpiringSoon | Expired</c>) as a field beside
    /// <c>state</c>. This schema has no such column: ExpiringSoon and Expired are DocumentState
    /// members, so the two fields are one thing modelled differently. Derived rather than stored,
    /// and null when the document carries no expiry date at all - a type that does not track expiry
    /// is not "Valid", it is not in the expiry machine.
    /// </summary>
    private static string? ExpiryStateOf(DocumentState state, DateOnly? expiryDate) => state switch
    {
        DocumentState.Expired => "Expired",
        DocumentState.ExpiringSoon => "ExpiringSoon",
        _ => expiryDate is null ? null : "Valid",
    };

    private static IReadOnlyList<string>? DescribeFilters(string? state) =>
        string.IsNullOrWhiteSpace(state) ? null : [$"state={state}"];
}
