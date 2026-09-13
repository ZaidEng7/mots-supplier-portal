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

/// <summary>
/// SCR-602: every tender in the country, whatever state it is in and whoever is running it.
///
/// <para>No organization predicate - the same inversion the governance overview documents, and the reason
/// <c>governance.read</c> is its own permission. What is new here is that the rows are NAMED: the tender, its
/// buying body, and its awarded value where there is one. The aggregate reads never carried any of that.</para>
/// </summary>
public sealed class ListMinistryRfqsHandler(AppDbContext db) : IListMinistryRfqsHandler
{
    public async Task<ListEnvelope<MinistryRfqRowDto>> HandleAsync(
        string? cursor, int? limit, bool withCount, string? state, string? q, CancellationToken ct)
    {
        var pageSize = ListEnvelope<MinistryRfqRowDto>.ClampPageSize(limit);
        var commercialVisible = await MinistryCommercialVisibility.IsOnAsync(db, ct);

        var query = db.Rfqs.AsNoTracking().AsQueryable();

        if (state is not null && Enum.TryParse<RfqState>(state, out var parsed))
        {
            query = query.Where(r => r.State == parsed);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim()}%";
            query = query.Where(r =>
                EF.Functions.ILike(r.TitleEn, pattern)
                || EF.Functions.ILike(r.TitleAr, pattern)
                || EF.Functions.ILike(r.ReferenceCode, pattern));
        }

        int? totalCount = withCount ? await query.CountAsync(ct) : null;

        // Newest first: a monitor is read from the top, and a tender published this morning is the one an
        // overseer is looking for.
        if (KeysetCursor.TryDecode(cursor, out var from))
        {
            query = query.Where(r =>
                r.CreatedAt < from.At || (r.CreatedAt == from.At && r.Id.CompareTo(from.Id) < 0));
        }

        var rows = await query
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Select(r => new
            {
                r.Id, r.ReferenceCode, r.TitleAr, r.TitleEn, r.State, r.CreatedAt,
                r.PublishedAt, r.SubmissionClosesAt, r.CurrencyCode, r.OrganizationId,
                Invited = db.Invitations.Count(i => i.RfqId == r.Id),
                Submitted = db.Proposals.Count(p => p.RfqId == r.Id && p.State != ProposalState.Draft
                                                    && p.State != ProposalState.Lapsed),
            })
            .Take(pageSize + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > pageSize;
        var items = hasMore ? rows[..pageSize] : rows;

        var organisations = await db.Organizations.AsNoTracking()
            .Where(o => items.Select(i => i.OrganizationId).Contains(o.Id))
            .Select(o => new { o.Id, o.LegalNameAr, o.LegalNameEn })
            .ToDictionaryAsync(o => o.Id, o => o, ct);

        var awardedValues = commercialVisible
            ? await AwardedValuesAsync(db, items.Select(i => i.Id).ToList(), ct)
            : [];

        var data = items.Select(r =>
        {
            organisations.TryGetValue(r.OrganizationId, out var org);
            return new MinistryRfqRowDto(
                r.ReferenceCode, r.TitleAr, r.TitleEn, r.State.ToString(),
                org?.LegalNameAr ?? "—", org?.LegalNameEn ?? "—",
                r.PublishedAt, r.SubmissionClosesAt, r.Invited, r.Submitted,
                awardedValues.TryGetValue(r.Id, out var value) ? value : null,
                r.CurrencyCode);
        }).ToList();

        var nextCursor = hasMore && items.Count > 0
            ? new KeysetCursor(items[^1].CreatedAt, items[^1].Id).Encode()
            : null;

        return new ListEnvelope<MinistryRfqRowDto>(
            data,
            new PaginationEnvelope("cursor", nextCursor, null, pageSize, totalCount, hasMore),
            new ListMetaEnvelope("-createdAt", null));
    }

    /// <summary>Awarded value per RFQ, for the tenders on this page that have an Awarded award.</summary>
    internal static async Task<Dictionary<Guid, decimal>> AwardedValuesAsync(
        AppDbContext db, IReadOnlyCollection<Guid> rfqIds, CancellationToken ct)
    {
        var awards = await db.Awards.AsNoTracking()
            .Where(a => rfqIds.Contains(a.RfqId) && a.State == AwardState.Awarded)
            .Select(a => new { a.RfqId, a.WinningProposalId })
            .ToListAsync(ct);

        var totals = await MinistryCommercialVisibility.TotalsByProposalAsync(
            db, awards.Select(a => a.WinningProposalId).ToList(), ct);

        return awards
            .Where(a => totals.ContainsKey(a.WinningProposalId))
            .GroupBy(a => a.RfqId)
            .ToDictionary(g => g.Key, g => g.Sum(a => totals[a.WinningProposalId]));
    }
}
