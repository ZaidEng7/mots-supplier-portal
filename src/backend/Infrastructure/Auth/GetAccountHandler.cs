using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Auth;

public sealed class GetAccountHandler(AppDbContext db) : IGetAccountHandler
{
    public async Task<AccountDto?> HandleAsync(Guid userId, CancellationToken ct) =>
        await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new AccountDto(u.FullName, u.Email!, u.Language, u.LanguageChosenAt != null))
            .FirstOrDefaultAsync(ct);
}
