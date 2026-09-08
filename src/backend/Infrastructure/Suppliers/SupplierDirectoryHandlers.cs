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

/// <summary>
/// SCR-307: the compliance directory.
///
/// <para><b>What this answers that the review queue cannot.</b> The queue is "what is waiting for me" and
/// a case leaves it when it is decided. Expiry happens afterwards: a document on an approved supplier
/// expires months later, the job moves it to Expired, BRULE-023 may suspend the supplier for it, and until
/// this screen existed nothing listed that. A reviewer's only route to an approved supplier was to already
/// know its reference code.</para>
///
/// <para><b>Document health is counted in one grouped query per page</b>, not by asking each supplier in
/// turn. The three counts are kept separate rather than summed: "expiring" is a prompt and "expired" is a
/// bar, and a single number would hide which one a row has.</para>
/// </summary>
public sealed class ListComplianceDirectoryHandler(AppDbContext db) : IListComplianceDirectoryHandler
{
    public async Task<ListEnvelope<ComplianceDirectoryItemDto>> HandleAsync(
        string? cursor, int? limit, bool withCount, string? onboardingState, string? documentHealth, string? q,
        CancellationToken ct)
    {
        var pageSize = ListEnvelope<ComplianceDirectoryItemDto>.ClampPageSize(limit);

        var query = db.Suppliers.AsNoTracking().AsQueryable();

        if (onboardingState is not null && Enum.TryParse<SupplierOnboardingState>(onboardingState, out var state))
        {
            query = query.Where(s => s.OnboardingState == state);
        }

        if (documentHealth is not null)
        {
            // The predicate is on the latest version of each document, because a superseded Expired row is
            // history rather than a problem - the renewal that replaced it is what counts.
            var needsAttention = db.SupplierDocuments.Where(d => d.IsLatestVersion
                && (d.State == DocumentState.Expired
                    || d.State == DocumentState.ExpiringSoon
                    || d.State == DocumentState.Rejected));

            query = documentHealth == "attention"
                ? query.Where(s => needsAttention.Any(d => d.SupplierId == s.Id))
                : query.Where(s => !needsAttention.Any(d => d.SupplierId == s.Id));
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim()}%";
            query = query.Where(s =>
                EF.Functions.ILike(s.DisplayNameEn, pattern)
                || EF.Functions.ILike(s.DisplayNameAr, pattern)
                || EF.Functions.ILike(s.ReferenceCode, pattern));
        }

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
                s.Id, s.ReferenceCode, s.DisplayNameAr, s.DisplayNameEn, s.CreatedAt,
                s.OnboardingState, s.LifecycleState,
            })
            .Take(pageSize + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > pageSize;
        var items = hasMore ? rows[..pageSize] : rows;
        var pageIds = items.Select(r => r.Id).ToList();

        var healthBySupplier = await db.SupplierDocuments.AsNoTracking()
            .Where(d => pageIds.Contains(d.SupplierId) && d.IsLatestVersion)
            .GroupBy(d => d.SupplierId)
            .Select(g => new
            {
                SupplierId = g.Key,
                Expired = g.Count(d => d.State == DocumentState.Expired),
                Expiring = g.Count(d => d.State == DocumentState.ExpiringSoon),
                Rejected = g.Count(d => d.State == DocumentState.Rejected),
            })
            .ToDictionaryAsync(x => x.SupplierId, x => x, ct);

        var data = items
            .Select(r =>
            {
                healthBySupplier.TryGetValue(r.Id, out var health);
                return new ComplianceDirectoryItemDto(
                    r.ReferenceCode, r.DisplayNameAr, r.DisplayNameEn,
                    r.OnboardingState.ToString(), r.LifecycleState.ToString(), r.CreatedAt,
                    health?.Expired ?? 0, health?.Expiring ?? 0, health?.Rejected ?? 0);
            })
            .ToList();

        var nextCursor = hasMore && items.Count > 0
            ? new SupplierDirectoryCursor(items[^1].DisplayNameEn, items[^1].Id).Encode()
            : null;

        return new ListEnvelope<ComplianceDirectoryItemDto>(
            data,
            new PaginationEnvelope("cursor", nextCursor, null, pageSize, totalCount, hasMore),
            new ListMetaEnvelope("displayNameEn", FiltersApplied(onboardingState, documentHealth, q)));
    }

    private static IReadOnlyList<string>? FiltersApplied(string? onboardingState, string? documentHealth, string? q)
    {
        var applied = new List<string>();
        if (onboardingState is not null) applied.Add("onboardingState");
        if (documentHealth is not null) applied.Add("documentHealth");
        if (!string.IsNullOrWhiteSpace(q)) applied.Add("q");
        return applied.Count == 0 ? null : applied;
    }
}
