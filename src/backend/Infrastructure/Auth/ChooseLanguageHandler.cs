// The first-run language chooser. It sets the language, stamps the choice, and touches nothing else.
//
// Idempotent by intent rather than by a guard: choosing twice is choosing, and the stamp keeps its original
// value so "when did this user first decide" stays answerable.

namespace MotsSupplierPortal.Infrastructure.Auth;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class ChooseLanguageHandler(AppDbContext db) : IChooseLanguageHandler
{
    public async Task<AccountDto?> HandleAsync(ChooseLanguageCommand command, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, ct);
        if (user is null) return null;

        user.Language = command.Language;
        user.LanguageChosenAt ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return new AccountDto(user.FullName, user.Email!, user.Language, true);
    }
}
