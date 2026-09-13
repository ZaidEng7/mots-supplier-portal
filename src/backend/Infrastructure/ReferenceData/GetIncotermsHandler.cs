// The delivery terms a bidder chooses from, which is also the list their bid is validated against.
//
// Active rows only, and the same test the write path enforces.
//
// A screen offering a term the write path refuses would be a form that fails on submission for a reason the bidder
// cannot see, which is the failure the free-text field had, wearing a dropdown.

namespace MotsSupplierPortal.Infrastructure.ReferenceData;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.ReferenceData;
using MotsSupplierPortal.Infrastructure.Persistence;

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
