// A member of staff accepts their invitation and sets a password.
//
// The work is shared with the supplier-side invitation. This class only maps that shared outcome onto this
// flow's own result type.

namespace MotsSupplierPortal.Infrastructure.Identity;

using Microsoft.AspNetCore.Identity;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;

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
