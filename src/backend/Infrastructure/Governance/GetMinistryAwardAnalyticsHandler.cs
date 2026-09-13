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

/// <summary>SCR-603: award and spend analytics, by month, category and buying body.</summary>
public sealed class GetMinistryAwardAnalyticsHandler(AppDbContext db) : IGetMinistryAwardAnalyticsHandler
{
    public async Task<MinistryAwardAnalyticsDto> HandleAsync(CancellationToken ct)
    {
        var commercialVisible = await MinistryCommercialVisibility.IsOnAsync(db, ct);

        var awards = await db.Awards.AsNoTracking()
            .Where(a => a.State == AwardState.Awarded)
            .Select(a => new { a.Id, a.RfqId, a.WinningProposalId, a.CreatedAt, a.AwardedAt })
            .ToListAsync(ct);

        var totals = commercialVisible
            ? await MinistryCommercialVisibility.TotalsByProposalAsync(
                db, awards.Select(a => a.WinningProposalId).ToList(), ct)
            : [];

        decimal? ValueOf(IEnumerable<Guid> proposalIds) =>
            commercialVisible ? proposalIds.Sum(id => totals.TryGetValue(id, out var v) ? v : 0m) : null;

        var rfqIds = awards.Select(a => a.RfqId).Distinct().ToList();

        var rfqOrganisations = await db.Rfqs.AsNoTracking()
            .Where(r => rfqIds.Contains(r.Id))
            .Select(r => new { r.Id, r.OrganizationId })
            .ToDictionaryAsync(r => r.Id, r => r.OrganizationId, ct);

        var organisationNames = await db.Organizations.AsNoTracking()
            .Select(o => new { o.Id, o.LegalNameEn })
            .ToDictionaryAsync(o => o.Id, o => o.LegalNameEn, ct);

        // A tender's categories come from its line items; an award counts once per category it touched,
        // which is the only honest way to attribute a single award across a multi-category tender.
        var rfqCategories = await db.RfqItems.AsNoTracking()
            .Where(i => rfqIds.Contains(i.RfqId))
            .Select(i => new { i.RfqId, i.CategoryCode })
            .Distinct()
            .ToListAsync(ct);

        // InvariantCulture, and this is not a style preference. `ToString("yyyy-MM")` formats in the
        // CURRENT culture's calendar, and this application runs with an Arabic culture available - so
        // the buckets came back as "1448-01" and "1448-02", Hijri years, on a chart whose axis a reader
        // takes for the Gregorian months every other date on the screen is written in. It was invisible
        // until the demonstration database had its first award: with no awards there are no buckets,
        // and an empty chart formats nothing.
        //
        // Grouped by AwardedAt rather than CreatedAt. CreatedAt is when the RECOMMENDATION row was
        // written; this chart is titled by award. An award recommended in June and executed in July
        // belongs to July, and the two are routinely different months. The fallback is for a row that
        // somehow reached Awarded without a date, which the aggregate does not allow.
        var byMonth = awards
            .GroupBy(a => MinistryAwardMonth.Of(a.AwardedAt ?? a.CreatedAt))
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new MinistrySpendBucketDto(g.Key, g.Count(), ValueOf(g.Select(a => a.WinningProposalId))))
            .ToList();

        var byOrganization = awards
            .GroupBy(a => rfqOrganisations.TryGetValue(a.RfqId, out var orgId)
                && organisationNames.TryGetValue(orgId, out var name) ? name : "—")
            .OrderByDescending(g => g.Count())
            .Select(g => new MinistrySpendBucketDto(g.Key, g.Count(), ValueOf(g.Select(a => a.WinningProposalId))))
            .ToList();

        // The names for the codes these buckets group on. Without them a Ministry reader meets
        // `tour_operations` on the axis of a chart whose whole reason for being a ranked bar is that a
        // category name is prose too long to fit under a column.
        var categoryNames = await db.Categories.AsNoTracking()
            .Select(c => new { c.Code, c.NameAr, c.NameEn })
            .ToDictionaryAsync(c => c.Code, c => c, ct);

        var byCategory = rfqCategories
            .Join(awards, c => c.RfqId, a => a.RfqId, (c, a) => new { c.CategoryCode, a.WinningProposalId })
            .GroupBy(x => x.CategoryCode)
            .OrderByDescending(g => g.Count())
            .Select(g => new MinistrySpendBucketDto(
                g.Key, g.Count(), ValueOf(g.Select(x => x.WinningProposalId)),
                // A code with no reference row keeps its code rather than rendering blank: a category
                // that was deleted from the reference list still awarded something, and hiding it would
                // change the total.
                categoryNames.TryGetValue(g.Key, out var name) ? name.NameAr : null,
                categoryNames.TryGetValue(g.Key, out var named) ? named.NameEn : null))
            .ToList();

        return new MinistryAwardAnalyticsDto(
            awards.Count,
            ValueOf(awards.Select(a => a.WinningProposalId)),
            byMonth, byCategory, byOrganization,
            commercialVisible);
    }
}
