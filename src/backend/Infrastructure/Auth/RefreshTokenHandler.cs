// Exchanging a refresh token for a new pair, and detecting theft.
//
// The token rotates on use: the presented one is revoked and a new one issued in the same family, in one save.
// The new token is created at the instant the old one was revoked, which is what lets a refusal below tell a
// rotated token from a revoked one. A rotation writes no audit row; it is the session ticking over, not something
// a person did.
//
// An account that has since been deactivated cannot refresh either, which is half of what makes deactivation
// immediate.
//
//
// A TOKEN THAT IS NO LONGER ACTIVE IS REFUSED IN ONE OF THREE WAYS
//
// Active is RefreshToken's own rule, the one the session list and the session counts use, so "this token can
// still be exchanged" and "this session is still open" cannot disagree.
//
// It was rotated away, and is presented again more than the grace period after it was. That is the classic
// theft signal: the legitimate holder and a thief cannot both use one token, so a second use means one of them is
// not the owner. The whole family is revoked, so neither keeps the session, and the reuse is recorded in the same
// save as the revocation.
//
// It was rotated away moments ago. That is a second request that left the browser carrying the same cookie as
// the one that rotated it: two tabs refreshing at once, or a page that fired several requests as its access token
// ran out. It is refused, and nothing else happens: the family stays alive, because its live token is the one the
// first request just handed back, and nothing is recorded, because nothing was stolen. It is answered as
// Superseded rather than Invalid so the endpoint leaves the browser's cookie alone; by the time this answer
// arrives the cookie may already hold the successor, and clearing it would sign out the tab that won.
//
// It expired, or it was revoked with no successor: signed out, revoked from the session list, ended by a password
// reset or by deactivation. The session is simply over. It is refused as invalid, the family is left as it is
// and nothing is recorded. Treating these as theft used to write a reuse row and revoke the family for what was
// an ordinary ended session.
//
//
// THE GRACE PERIOD
//
// Ten seconds covers two requests leaving one browser at the same moment, a slow network on the second included.
// A token presented inside the window is still only refused; it never refreshes.
//
// The window has a cost, and it is the mirror case. If a thief's copy is the one that rotates first and the
// owner's browser presents the same token within those seconds, the owner is refused without the family being
// revoked, so that one replay goes unrecorded and the owner has to sign in again. Past the window either order is
// caught. The window is kept short for that reason, and it is a named value so that trade is visible.

namespace MotsSupplierPortal.Infrastructure.Auth;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class RefreshTokenHandler(
    AppDbContext db,
    UserManager<AppUser> userManager,
    IAuditLogger auditLogger,
    LoginHandler loginHandler) : IRefreshTokenHandler
{
    public static readonly TimeSpan ParallelRefreshGrace = TimeSpan.FromSeconds(10);

    public async Task<RefreshTokenResult> HandleAsync(RefreshTokenCommand command, CancellationToken ct)
    {
        var hash = TokenHasher.Hash(command.RefreshToken);
        var presented = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (presented is null)
        {
            return new RefreshTokenResult.Invalid();
        }

        if (!presented.IsActive)
        {
            return await RefuseAsync(presented, ct);
        }

        var user = await userManager.FindByIdAsync(presented.UserId.ToString());
        if (user is null || !user.IsActive)
        {
            return new RefreshTokenResult.Invalid();
        }

        var rotatedAt = DateTimeOffset.UtcNow;
        presented.RevokedAt = rotatedAt;
        var tokens = await loginHandler.IssueTokenPairAsync(
            user, presented.FamilyId, command.Ip, command.UserAgent, ct, issuedAt: rotatedAt);
        await db.SaveChangesAsync(ct);

        return new RefreshTokenResult.Success(tokens);
    }

    private async Task<RefreshTokenResult> RefuseAsync(RefreshToken presented, CancellationToken ct)
    {
        if (presented.RevokedAt is not { } revokedAt)
        {
            return new RefreshTokenResult.Invalid();
        }

        var rotatedAway = await db.RefreshTokens.AnyAsync(
            t => t.FamilyId == presented.FamilyId && t.Id != presented.Id && t.CreatedAt >= revokedAt, ct);

        if (!rotatedAway)
        {
            return new RefreshTokenResult.Invalid();
        }

        var now = DateTimeOffset.UtcNow;
        if (now - revokedAt <= ParallelRefreshGrace)
        {
            return new RefreshTokenResult.Superseded();
        }

        var family = await db.RefreshTokens
            .Where(t => t.FamilyId == presented.FamilyId && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var t in family) t.RevokedAt = now;

        await auditLogger.LogAsync("User", presented.UserId, SessionAuditActions.RefreshReuseDetected, presented.UserId, ct: ct);
        await db.SaveChangesAsync(ct);

        return new RefreshTokenResult.ReuseDetected();
    }
}
