using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Admin;

/// <summary>SCR-716's public read - see UiStringBundleDto for why it is anonymous.</summary>
public sealed class GetUiStringBundleHandler(AppDbContext db) : IGetUiStringBundleHandler
{
    public async Task<UiStringBundleDto> HandleAsync(string language, CancellationToken ct)
    {
        var strings = await db.UiStringOverrides.AsNoTracking()
            .Where(o => o.Language == language)
            .ToDictionaryAsync(o => o.Key, o => o.Value, ct);

        // An empty dictionary is the normal answer, not a failure: no overrides means the shipped bundle
        // stands, which is what a fresh deployment should do.
        return new UiStringBundleDto(language, strings);
    }
}
