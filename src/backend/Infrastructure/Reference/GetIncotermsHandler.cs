using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Reference;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Reference;

/// <summary>
/// T-072: the list a bidder chooses from, which is also the list their bid is validated against.
///
/// <para>Active rows only, and the same predicate <c>IncotermRule</c> enforces on write. A screen
/// that offered a term the write path refuses would be a form that fails on submit for a reason the
/// bidder cannot see - the failure mode the free-text field had, wearing a select.</para>
/// </summary>
public sealed class GetIncotermsHandler(AppDbContext db) : IGetIncotermsHandler
{
    public async Task<IReadOnlyList<IncotermDto>> HandleAsync(CancellationToken ct)
    {
        return await db.Incoterms
            .Where(i => i.IsActive)
            .OrderBy(i => i.Code)
            .Select(i => new IncotermDto(i.Id, i.Code, i.NameAr, i.NameEn))
            .ToListAsync(ct);
    }
}
