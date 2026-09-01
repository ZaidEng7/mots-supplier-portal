using Microsoft.AspNetCore.Identity;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Infrastructure.Identity;

/// <summary>Task #28: the actual work is InviteAcceptance.AcceptAsync, shared with
/// AcceptSupplierUserInviteHandler - this class only maps that shared outcome onto this flow's
/// own result type.</summary>
public sealed class AcceptStaffInviteHandler(UserManager<AppUser> userManager, ISecurityTokenService securityTokenService) : IAcceptStaffInviteHandler
{
    public async Task<AcceptStaffInviteResult> HandleAsync(AcceptStaffInviteCommand command, CancellationToken ct)
    {
        var core = await InviteAcceptance.AcceptAsync(
            userManager, securityTokenService, command.Token, SecurityTokenPurpose.StaffInvite, command.Password, ct);

        return core.Outcome switch
        {
            AcceptInviteOutcome.Success => new AcceptStaffInviteResult.Success(),
            AcceptInviteOutcome.WeakPassword => new AcceptStaffInviteResult.WeakPassword(core.Errors),
            _ => new AcceptStaffInviteResult.InvalidOrExpiredToken(),
        };
    }
}
