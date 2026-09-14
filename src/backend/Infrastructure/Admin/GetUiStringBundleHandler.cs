// The public read of the reworded interface strings for one language.
//
// Anonymous, because the interface needs it before anybody has signed in; the read model's own header explains
// that.
//
// An empty answer is the normal one rather than a failure: no overrides means the shipped bundle stands, which is
// what a fresh deployment should do.

namespace MotsSupplierPortal.Infrastructure.Admin;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class GetUiStringBundleHandler(AppDbContext db) : IGetUiStringBundleHandler
{
    public async Task<UiStringBundleDto> HandleAsync(string language, CancellationToken ct)
    {
        var strings = await db.UiStringOverrides.AsNoTracking()
            .Where(o => o.Language == language)
            .ToDictionaryAsync(o => o.Key, o => o.Value, ct);

        return new UiStringBundleDto(language, strings);
    }
}
