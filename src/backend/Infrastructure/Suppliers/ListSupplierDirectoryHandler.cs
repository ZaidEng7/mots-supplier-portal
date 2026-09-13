using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>
/// SCR-402: the procurement directory.
///
/// <para><b>Approved onboarding only, and that is a rule rather than a filter.</b> An invitation can only
/// be sent to an approved supplier - <c>InviteSupplierHandler</c> refuses anything else - so a directory
/// listing applicants would be a list of companies a buyer cannot act on. The lifecycle state is listed
/// rather than filtered out for the opposite reason: a suspended supplier a buyer knows is registered must
/// be findable, with the suspension visible, instead of absent with no explanation.</para>
/// </summary>
public sealed class ListSupplierDirectoryHandler(AppDbContext db) : IListSupplierDirectoryHandler
{
    public async Task<ListEnvelope<SupplierDirectoryItemDto>> HandleAsync(
        string? cursor, int? limit, bool withCount, string? category, string? lifecycleState, string? q,
        CancellationToken ct)
    {
        var pageSize = ListEnvelope<SupplierDirectoryItemDto>.ClampPageSize(limit);

        var query = db.Suppliers.AsNoTracking()
            .Where(s => s.OnboardingState == SupplierOnboardingState.Approved);

        if (lifecycleState is not null && Enum.TryParse<SupplierLifecycleState>(lifecycleState, out var lifecycle))
        {
            query = query.Where(s => s.LifecycleState == lifecycle);
        }

        if (category is not null)
        {
            // Through the link table rather than a column: a supplier carries several categories and
            // FEAT-04.7 records them as links for that reason.
            query = query.Where(s => db.CategoryLinks.Any(l => l.SupplierId == s.Id && l.CategoryCode == category));
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            // Both display names and the reference code, case-insensitively. A buyer searching for a
            // company types the name they know, which may be in either language, and a procurement officer
            // with a code in an email types the code.
            var pattern = $"%{q.Trim()}%";
            query = query.Where(s =>
                EF.Functions.ILike(s.DisplayNameEn, pattern)
                || EF.Functions.ILike(s.DisplayNameAr, pattern)
                || EF.Functions.ILike(s.ReferenceCode, pattern));
        }

        // §6.1: counted over the filtered set before the cursor narrows it, and only when asked.
        int? totalCount = withCount ? await query.CountAsync(ct) : null;

        if (SupplierDirectoryCursor.TryDecode(cursor, out var from))
        {
            query = query.Where(s =>
                string.Compare(s.DisplayNameEn, from.Name) > 0
                || (s.DisplayNameEn == from.Name && s.Id.CompareTo(from.Id) > 0));
        }

        var rows = await query
            .OrderBy(s => s.DisplayNameEn).ThenBy(s => s.Id)
            .Select(s => new
            {
                s.Id,
                s.ReferenceCode,
                s.DisplayNameAr,
                s.DisplayNameEn,
                s.LifecycleState,
                CategoryCodes = db.CategoryLinks.Where(l => l.SupplierId == s.Id)
                    .OrderBy(l => l.CategoryCode).Select(l => l.CategoryCode).ToList(),
                // Active offerings only: a buyer browsing for capability is asking what this supplier
                // currently offers, and a deactivated row is a catalogue entry they withdrew.
                OfferingCount = db.Offerings.Count(o => o.SupplierId == s.Id && o.IsActive),
                // The head office if there is one, otherwise any address. A directory row showing no city
                // for a company that recorded a branch address reads as missing data.
                Address = db.Addresses.Where(a => a.SupplierId == s.Id)
                    .OrderBy(a => a.Kind == AddressKind.HeadOffice ? 0 : 1)
                    .Select(a => new { a.City, a.RegionCode })
                    .FirstOrDefault(),
            })
            .Take(pageSize + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > pageSize;
        var items = hasMore ? rows[..pageSize] : rows;

        var data = items
            .Select(r => new SupplierDirectoryItemDto(
                r.ReferenceCode, r.DisplayNameAr, r.DisplayNameEn, r.LifecycleState.ToString(),
                r.CategoryCodes, r.OfferingCount, r.Address?.City, r.Address?.RegionCode))
            .ToList();

        var nextCursor = hasMore && items.Count > 0
            ? new SupplierDirectoryCursor(items[^1].DisplayNameEn, items[^1].Id).Encode()
            : null;

        return new ListEnvelope<SupplierDirectoryItemDto>(
            data,
            new PaginationEnvelope("cursor", nextCursor, null, pageSize, totalCount, hasMore),
            new ListMetaEnvelope("displayNameEn", FiltersApplied(category, lifecycleState, q)));
    }

    private static IReadOnlyList<string>? FiltersApplied(string? category, string? lifecycleState, string? q)
    {
        var applied = new List<string>();
        if (category is not null) applied.Add("category");
        if (lifecycleState is not null) applied.Add("lifecycleState");
        if (!string.IsNullOrWhiteSpace(q)) applied.Add("q");
        return applied.Count == 0 ? null : applied;
    }
}
