using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Auth;

/// <summary>
/// SCR-010's first-run chooser. Sets the language and stamps the choice, and touches nothing else.
///
/// <para>Idempotent by intent rather than by guard: choosing twice is choosing, and the stamp keeps
/// its original value so "when did this user first decide" stays answerable.</para>
/// </summary>
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
