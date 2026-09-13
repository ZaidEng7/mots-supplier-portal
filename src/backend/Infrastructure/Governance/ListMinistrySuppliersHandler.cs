using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Governance;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Infrastructure.Suppliers;

namespace MotsSupplierPortal.Infrastructure.Governance;

/// <summary>SCR-601: the supplier registry as the Ministry reads it - population, categories, standing, and
/// what each supplier has won.</summary>
public sealed class ListMinistrySuppliersHandler(AppDbContext db) : IListMinistrySuppliersHandler
{
    public async Task<ListEnvelope<MinistrySupplierRowDto>> HandleAsync(
        string? cursor, int? limit, bool withCount, string? lifecycleState, string? q, CancellationToken ct)
    {
        var pageSize = ListEnvelope<MinistrySupplierRowDto>.ClampPageSize(limit);
        var commercialVisible = await MinistryCommercialVisibility.IsOnAsync(db, ct);

        var query = db.Suppliers.AsNoTracking().AsQueryable();

        if (lifecycleState is not null && Enum.TryParse<SupplierLifecycleState>(lifecycleState, out var parsed))
        {
            query = query.Where(s => s.LifecycleState == parsed);
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
                CategoryCodes = db.CategoryLinks.Where(l => l.SupplierId == s.Id)
                    .OrderBy(l => l.CategoryCode).Select(l => l.CategoryCode).ToList(),
                Submitted = db.Proposals.Count(p => p.SupplierId == s.Id && p.State != ProposalState.Draft
                                                    && p.State != ProposalState.Lapsed),
            })
            .Take(pageSize + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > pageSize;
        var items = hasMore ? rows[..pageSize] : rows;
        var pageIds = items.Select(i => i.Id).ToList();

        // Awards won, and their value. The count is an aggregate the Ministry has always been granted; the
        // value is the part the flag governs.
        var winningProposals = await db.Awards.AsNoTracking()
            .Where(a => a.State == AwardState.Awarded)
            .Join(db.Proposals.AsNoTracking().Where(p => pageIds.Contains(p.SupplierId)),
                a => a.WinningProposalId, p => p.Id,
                (a, p) => new { p.SupplierId, p.Id })
            .ToListAsync(ct);

        var totals = commercialVisible
            ? await MinistryCommercialVisibility.TotalsByProposalAsync(
                db, winningProposals.Select(w => w.Id).ToList(), ct)
            : [];

        var awardsBySupplier = winningProposals.GroupBy(w => w.SupplierId)
            .ToDictionary(g => g.Key, g => new
            {
                Count = g.Count(),
                Value = g.Sum(w => totals.TryGetValue(w.Id, out var v) ? v : 0m),
            });

        var data = items.Select(s =>
        {
            awardsBySupplier.TryGetValue(s.Id, out var won);
            return new MinistrySupplierRowDto(
                s.ReferenceCode, s.DisplayNameAr, s.DisplayNameEn,
                s.OnboardingState.ToString(), s.LifecycleState.ToString(),
                s.CategoryCodes, s.CreatedAt, s.Submitted,
                won?.Count ?? 0,
                commercialVisible ? won?.Value ?? 0m : null);
        }).ToList();

        var nextCursor = hasMore && items.Count > 0
            ? new SupplierDirectoryCursor(items[^1].DisplayNameEn, items[^1].Id).Encode()
            : null;

        return new ListEnvelope<MinistrySupplierRowDto>(
            data,
            new PaginationEnvelope("cursor", nextCursor, null, pageSize, totalCount, hasMore),
            new ListMetaEnvelope("displayNameEn", null));
    }
}
