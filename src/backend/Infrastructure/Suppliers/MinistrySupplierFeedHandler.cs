// Loads the registry for the ministry's supplier feed, one supplier at a time.
//
// EVERY SUPPLIER AT EVERY ONBOARDING STATE, because their feed carries ApprovalStatus as a column: a file that
// quietly held only approved suppliers would be loaded as the whole registry and every count built on it would
// be wrong in a direction nobody could see.
//
// ORDERED BY ReferenceCode so two runs a minute apart produce the same rows in the same order. An unordered
// export diffs as though everything changed, which matters here because a nightly job will be comparing them.
//
// AsSplitQuery for the three child collections, so a supplier with four categories and two addresses does not
// come back as eight rows of the same supplier.
//
// The lookups are read once, ahead of the rows, because the category and governorate names are the same for
// every supplier and reading them per row would be thousands of repeats of two small tables.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class MinistrySupplierFeedHandler(AppDbContext db) : IMinistrySupplierFeedHandler
{
    public async Task<(IReadOnlyDictionary<string, string> CategoryNameEn, IReadOnlyDictionary<string, string> RegionNameEn)>
        GetLookupsAsync(CancellationToken ct)
    {
        var categories = await db.Categories
            .AsNoTracking()
            .Select(c => new { c.Code, c.NameEn })
            .ToDictionaryAsync(c => c.Code, c => c.NameEn, ct);

        var regions = await db.Regions
            .AsNoTracking()
            .Select(r => new { r.Code, r.NameEn })
            .ToDictionaryAsync(r => r.Code, r => r.NameEn, ct);

        return (categories, regions);
    }

    public async Task<IReadOnlyList<MinistrySupplierFeedRecord>> PageAsync(
        string? afterReferenceCode, DateTimeOffset? modifiedSince, int limit, CancellationToken ct)
    {
        var (categoryNames, regionNames) = await GetLookupsAsync(ct);

        var query = db.Suppliers
            .AsNoTracking()
            .AsSplitQuery()
            .Include(s => s.Addresses)
            .Include(s => s.Representatives)
            .Include(s => s.CategoryLinks)
            .OrderBy(s => s.ReferenceCode)
            .AsQueryable();

        if (afterReferenceCode is not null)
        {
            query = query.Where(s => string.Compare(s.ReferenceCode, afterReferenceCode) > 0);
        }

        if (modifiedSince is { } since)
        {
            query = query.Where(s => s.UpdatedAt > since);
        }

        var suppliers = await query.Take(limit).ToListAsync(ct);

        return [.. suppliers.Select(s => new MinistrySupplierFeedRecord(s, categoryNames, regionNames))];
    }

    public async IAsyncEnumerable<MinistrySupplierFeedRecord> StreamAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var (categoryNames, regionNames) = await GetLookupsAsync(ct);

        var suppliers = db.Suppliers
            .AsNoTracking()
            .AsSplitQuery()
            .Include(s => s.Addresses)
            .Include(s => s.Representatives)
            .Include(s => s.CategoryLinks)
            .OrderBy(s => s.ReferenceCode)
            .AsAsyncEnumerable();

        await foreach (var supplier in suppliers.WithCancellation(ct))
        {
            yield return new MinistrySupplierFeedRecord(supplier, categoryNames, regionNames);
        }
    }
}
