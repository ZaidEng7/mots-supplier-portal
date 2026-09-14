// Award and spend analytics for the ministry, by month, by category and by buying body.
//
//
// A TENDER'S CATEGORIES COME FROM ITS LINES
//
// An award counts once per category its tender touched, which is the only honest way to attribute a single award
// across a multi-category tender.
//
// A category code with no reference row keeps its code rather than rendering blank. A category deleted from the
// reference list still awarded something, and hiding it would change the total.
//
// The names for those codes are resolved, because without them a reader meets a raw code on the axis of a chart
// whose whole reason for being a ranked bar is that a category name is prose too long to fit under a column.
//
//
// THE MONTH BUCKETS ARE FORMATTED CULTURE-INVARIANTLY, AND THAT IS NOT A STYLE PREFERENCE
//
// The ordinary short-date format uses the CURRENT culture's calendar, and this application runs with an Arabic
// culture available, so the buckets came back as Hijri years on a chart whose axis a reader takes for the
// Gregorian months every other date on the screen is written in.
//
// It was invisible until the demonstration database had its first award: with no awards there are no buckets, and
// an empty chart formats nothing.
//
//
// GROUPED BY WHEN THE AWARD WAS MADE, NOT WHEN THE ROW WAS CREATED
//
// The row is created when the RECOMMENDATION is written, and this chart is titled by award. An award recommended in
// June and executed in July belongs to July, and the two are routinely different months.
//
// The fallback covers a row that somehow reached the awarded state with no date, which the aggregate does not
// allow.

namespace MotsSupplierPortal.Infrastructure.Governance;

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

        var rfqCategories = await db.RfqItems.AsNoTracking()
            .Where(i => rfqIds.Contains(i.RfqId))
            .Select(i => new { i.RfqId, i.CategoryCode })
            .Distinct()
            .ToListAsync(ct);

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

        var categoryNames = await db.Categories.AsNoTracking()
            .Select(c => new { c.Code, c.NameAr, c.NameEn })
            .ToDictionaryAsync(c => c.Code, c => c, ct);

        var byCategory = rfqCategories
            .Join(awards, c => c.RfqId, a => a.RfqId, (c, a) => new { c.CategoryCode, a.WinningProposalId })
            .GroupBy(x => x.CategoryCode)
            .OrderByDescending(g => g.Count())
            .Select(g => new MinistrySpendBucketDto(
                g.Key, g.Count(), ValueOf(g.Select(x => x.WinningProposalId)),
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
