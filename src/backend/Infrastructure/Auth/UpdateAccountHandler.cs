using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Auth;

/// <summary>
/// SCR-902's update. Two fields, and neither is an authorization fact: renaming yourself and
/// switching your own interface language decide nothing about what you may do.
///
/// <para><b>Written through the tracked entity rather than ExecuteUpdateAsync.</b> The row is small and
/// the write is one round trip either way, so the reason is the interceptor: ExpectedVersionInterceptor
/// runs on SaveChangesAsync and enforces the app-managed version guard, and ExecuteUpdateAsync goes round
/// it. Not for audit - auditing in this codebase is explicit through IAuditLogger, never an interceptor,
/// which was checked rather than assumed after an earlier version of this comment claimed otherwise.
/// Renaming yourself is deliberately NOT audited: FR-AUD-001 scopes the trail to procurement
/// accountability, and adding identity changes to it is a decision for whoever owns that scope.</para>
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
