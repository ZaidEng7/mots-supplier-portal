// A user updates their own name and interface language.
//
// Two fields, and neither is an authorisation fact: renaming yourself and switching your own language decide
// nothing about what you may do.
//
// Saving the account screen counts as choosing a language, so the first-run chooser does not reappear for
// somebody who has already been through the settings.
//
//
// WRITTEN THROUGH THE TRACKED ENTITY RATHER THAN A DIRECT UPDATE
//
// The row is small and it is one round trip either way, so the reason is the interceptor: the expected-version
// guard runs on the save and a direct update goes round it.
//
// Not for the audit trail. Auditing here is always explicit through the logger and never an interceptor, which
// was checked rather than assumed after an earlier version of this note claimed otherwise.
//
// Renaming yourself is deliberately NOT audited. The written requirement scopes the trail to procurement
// accountability, and adding identity changes to it is a decision for whoever owns that scope.

namespace MotsSupplierPortal.Infrastructure.Auth;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class UpdateAccountHandler(AppDbContext db) : IUpdateAccountHandler
{
    public async Task<AccountDto?> HandleAsync(UpdateAccountCommand command, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, ct);
        if (user is null) return null;

        user.FullName = command.FullName;
        user.Language = command.Language;
        user.LanguageChosenAt ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return new AccountDto(user.FullName, user.Email!, user.Language, true);
    }
}
