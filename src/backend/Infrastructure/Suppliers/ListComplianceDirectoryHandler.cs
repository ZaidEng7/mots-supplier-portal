// The reviewer's directory of the whole registry, with each supplier's document health.
//
//
// WHAT THIS ANSWERS THAT THE REVIEW QUEUE CANNOT
//
// The queue answers "what is waiting for me", and a case leaves it the moment it is decided.
//
// Expiry happens afterwards. A document on an approved supplier expires months later, the job moves it to
// expired, the supplier may be suspended for it, and until this screen existed nothing listed that. A
// reviewer's only route to an approved supplier was to already know its reference code.
//
//
// DOCUMENT HEALTH
//
// Counted in one grouped query per page rather than by asking each supplier in turn.
//
// The three counts are kept apart rather than summed, because expiring is a prompt and expired is a bar, and
// one number would hide which of them a row has.
//
// Both the filter and the counts look only at the latest version of each document. A superseded expired row
// is history rather than a problem: the renewal that replaced it is what counts.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

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
