// Creating the account behind an invitation: the part the staff and supplier invitations share.
//
// Both mint an account with an unusable random password and the email treated as already confirmed, the
// invitation itself standing in for a separate verification step because it was sent to a real address.
//
// Assigning the role, writing the audit row and queueing the email stay with each caller. Only what is
// genuinely identical between the two lives here.

namespace MotsSupplierPortal.Infrastructure.Identity;

using Microsoft.AspNetCore.Identity;
using MotsSupplierPortal.Domain.Identity;

public static class InviteUserCreation
{
    public sealed record Outcome(bool Succeeded, AppUser? User);

    public static async Task<Outcome> CreateInvitedUserAsync(
        UserManager<AppUser> userManager, string email, string fullName, Guid? supplierId, Guid? organizationId = null)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var existing = await userManager.FindByEmailAsync(normalizedEmail);
        if (existing is not null)
        {
            return new Outcome(false, null);
        }

        var user = new AppUser
        {
            Id = Guid.CreateVersion7(),
            UserName = normalizedEmail,
            Email = normalizedEmail,
            FullName = fullName,
            SupplierId = supplierId,
            OrganizationId = organizationId,
            EmailConfirmed = true,
            IsActive = true,
        };

        var randomPassword = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var createResult = await userManager.CreateAsync(user, randomPassword);
        if (!createResult.Succeeded)
        {
            return new Outcome(false, null);
        }

        return new Outcome(true, user);
    }
}
