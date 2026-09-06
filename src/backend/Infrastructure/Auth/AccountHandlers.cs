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

/// <summary>
/// SCR-902's update. Two fields, and neither is an authorization fact: renaming yourself and
/// switching your own interface language decide nothing about what you may do.
///
/// <para><b>Written through the tracked entity rather than ExecuteUpdateAsync.</b> The user row is
/// small, the write is a single round trip either way, and going through the change tracker keeps
/// SaveChangesAsync's interceptors - audit among them - in the path. An ExecuteUpdateAsync here would
/// silently opt this write out of the audit trail that FR-AUD-003 puts on the account screen itself.</para>
/// </summary>
public sealed class UpdateAccountHandler(AppDbContext db) : IUpdateAccountHandler
{
    public async Task<AccountDto?> HandleAsync(UpdateAccountCommand command, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, ct);
        if (user is null) return null;

        user.FullName = command.FullName;
        user.Language = command.Language;
        // Saving the account screen counts as choosing, so the first-run chooser does not reappear for
        // someone who has already been through the settings.
        user.LanguageChosenAt ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return new AccountDto(user.FullName, user.Email!, user.Language, true);
    }
}

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
