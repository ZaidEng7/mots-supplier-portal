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
/// Shared by the four SCR-601/602/603/606 handlers: is the Ministry permitted to see money, and what is a
/// bid worth.
///
/// <para><b>The flag is read on every request rather than cached.</b> It is a policy switch, and the point of
/// a policy switch is that turning it off takes effect now - a cached "yes" would keep disclosing for the
/// lifetime of a process after somebody decided to stop.</para>
/// </summary>
/// <summary>
/// The month an award belongs to, as the analytics bucket it.
///
/// <para>Public and named so it can be tested, because the defect it closes cannot be seen from the
/// outside without award data. <c>ToString("yyyy-MM")</c> formats in the CURRENT culture's calendar, and
/// this application runs with Arabic cultures available - so the buckets came back "1448-01", Hijri
/// years, on an axis a reader takes for the Gregorian months every other date on the screen uses. With
/// no awards in a database there are no buckets and an empty chart formats nothing, so the bug shipped
/// and stayed invisible until the demonstration data had its first award.</para>
/// </summary>
public static class MinistryAwardMonth
{
    public static string Of(DateTimeOffset when) => when.ToString("yyyy-MM", CultureInfo.InvariantCulture);
}

internal static class MinistryCommercialVisibility
{
    public static Task<bool> IsOnAsync(AppDbContext db, CancellationToken ct) =>
        SupplierFieldConfigLookup.IsEnabledAsync(
            db, FieldConfigCategory.GovernanceVisibility, "commercialValues", defaultValue: false, ct);

    /// <summary>Bid totals for the named proposals, summed from their lines.
    ///
    /// <para>Summed in memory over at most a page's worth of proposals, for the same reason
    /// <c>GetGovernanceOverviewHandler</c> gives: the SQL shape that groups across two DbSets did not
    /// translate and answered 500 once the dataset grew.</para></summary>
    public static async Task<Dictionary<Guid, decimal>> TotalsByProposalAsync(
        AppDbContext db, IReadOnlyCollection<Guid> proposalIds, CancellationToken ct)
    {
        if (proposalIds.Count == 0) return [];

        var lines = await db.ProposalItems.AsNoTracking()
            .Where(i => proposalIds.Contains(i.ProposalId))
            .Select(i => new { i.ProposalId, i.Quantity, i.UnitPrice, i.Discount })
            .ToListAsync(ct);

        return lines
            .GroupBy(l => l.ProposalId)
            .ToDictionary(g => g.Key, g => g.Sum(l => (l.Quantity * l.UnitPrice) - (l.Discount ?? 0m)));
    }
}

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
        if (MinistryRfqCursor.TryDecode(cursor, out var from))
        {
            query = query.Where(r =>
                r.CreatedAt < from.CreatedAt || (r.CreatedAt == from.CreatedAt && r.Id.CompareTo(from.Id) < 0));
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
            ? new MinistryRfqCursor(items[^1].CreatedAt, items[^1].Id).Encode()
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

        var byCategory = rfqCategories
            .Join(awards, c => c.RfqId, a => a.RfqId, (c, a) => new { c.CategoryCode, a.WinningProposalId })
            .GroupBy(x => x.CategoryCode)
            .OrderByDescending(g => g.Count())
            .Select(g => new MinistrySpendBucketDto(g.Key, g.Count(), ValueOf(g.Select(x => x.WinningProposalId))))
            .ToList();

        return new MinistryAwardAnalyticsDto(
            awards.Count,
            ValueOf(awards.Select(a => a.WinningProposalId)),
            byMonth, byCategory, byOrganization,
            commercialVisible);
    }
}

/// <summary>
/// SCR-606: one tender, read-only, with every bid on it.
///
/// <para><b>This is where D-66's scope actually bites.</b> The bids are listed with the bidding supplier
/// NAMED and its total shown, on a tender in any state including one still open. Under the narrower scope
/// D-57 offered, this list would have been empty until the award; under the widest, which is what was
/// chosen, it is populated from the first submitted bid.</para>
/// </summary>
public sealed class GetMinistryRfqDetailHandler(AppDbContext db) : IGetMinistryRfqDetailHandler
{
    public async Task<MinistryRfqDetailDto?> HandleAsync(string referenceCode, CancellationToken ct)
    {
        var commercialVisible = await MinistryCommercialVisibility.IsOnAsync(db, ct);

        var rfq = await db.Rfqs.AsNoTracking()
            .Where(r => r.ReferenceCode == referenceCode)
            .Select(r => new
            {
                r.Id, r.ReferenceCode, r.TitleAr, r.TitleEn, r.DescriptionAr, r.DescriptionEn,
                r.State, r.PublishedAt, r.SubmissionClosesAt, r.CurrencyCode, r.OrganizationId,
            })
            .FirstOrDefaultAsync(ct);

        if (rfq is null) return null;

        var organisation = await db.Organizations.AsNoTracking()
            .Where(o => o.Id == rfq.OrganizationId)
            .Select(o => new { o.LegalNameAr, o.LegalNameEn })
            .FirstOrDefaultAsync(ct);

        var items = await db.RfqItems.AsNoTracking()
            .Where(i => i.RfqId == rfq.Id)
            .OrderBy(i => i.LineNo)
            .Select(i => new MinistryRfqItemDto(i.TitleAr, i.TitleEn, i.CategoryCode, i.Quantity, i.UnitOfMeasureCode))
            .ToListAsync(ct);

        // Everything except a Draft: a draft bid has not been offered to anybody, and showing it would
        // disclose a supplier's unfinished thinking - which no reading of D-57 covers.
        var proposals = await db.Proposals.AsNoTracking()
            .Where(p => p.RfqId == rfq.Id && p.State != ProposalState.Draft)
            .Select(p => new { p.Id, p.ReferenceCode, p.SupplierId, p.State, p.SubmittedAt })
            .ToListAsync(ct);

        var suppliers = await db.Suppliers.AsNoTracking()
            .Where(s => proposals.Select(p => p.SupplierId).Contains(s.Id))
            .Select(s => new { s.Id, s.ReferenceCode, s.DisplayNameAr, s.DisplayNameEn })
            .ToDictionaryAsync(s => s.Id, s => s, ct);

        var totals = commercialVisible
            ? await MinistryCommercialVisibility.TotalsByProposalAsync(db, proposals.Select(p => p.Id).ToList(), ct)
            : [];

        var winningProposalIds = await db.Awards.AsNoTracking()
            .Where(a => a.RfqId == rfq.Id && a.State == AwardState.Awarded)
            .Select(a => a.WinningProposalId)
            .ToListAsync(ct);

        var bids = proposals
            .OrderBy(p => p.SubmittedAt ?? DateTimeOffset.MaxValue)
            .Select(p =>
            {
                suppliers.TryGetValue(p.SupplierId, out var supplier);
                return new MinistryBidDto(
                    p.ReferenceCode,
                    supplier?.ReferenceCode ?? "—",
                    supplier?.DisplayNameAr ?? "—",
                    supplier?.DisplayNameEn ?? "—",
                    p.State.ToString(),
                    p.SubmittedAt,
                    totals.TryGetValue(p.Id, out var value) ? value : null,
                    winningProposalIds.Contains(p.Id));
            })
            .ToList();

        var awardedValues = commercialVisible
            ? await ListMinistryRfqsHandler.AwardedValuesAsync(db, [rfq.Id], ct)
            : [];

        var summary = new MinistryRfqRowDto(
            rfq.ReferenceCode, rfq.TitleAr, rfq.TitleEn, rfq.State.ToString(),
            organisation?.LegalNameAr ?? "—", organisation?.LegalNameEn ?? "—",
            rfq.PublishedAt, rfq.SubmissionClosesAt,
            await db.Invitations.CountAsync(i => i.RfqId == rfq.Id, ct),
            bids.Count,
            awardedValues.TryGetValue(rfq.Id, out var awarded) ? awarded : null,
            rfq.CurrencyCode);

        return new MinistryRfqDetailDto(
            summary, rfq.DescriptionAr, rfq.DescriptionEn, items, bids, commercialVisible);
    }
}
