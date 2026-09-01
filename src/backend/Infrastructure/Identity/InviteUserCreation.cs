using Microsoft.AspNetCore.Identity;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Infrastructure.Identity;

/// <summary>Shared account-creation core for InviteStaffHandler and InviteSupplierUserHandler:
/// both mint an AppUser with an unusable random password and EmailConfirmed=true, the invite
/// itself (sent to a real address) standing in for a separate verification step. Role assignment,
/// audit logging, and background email enqueue stay in each caller - only what is genuinely
/// identical between the two moves here.</summary>
public static class InviteUserCreation
{
    public sealed record Outcome(bool Succeeded, AppUser? User);

    public static async Task<Outcome> CreateInvitedUserAsync(
        UserManager<AppUser> userManager, string email, string fullName, Guid? supplierId)
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
