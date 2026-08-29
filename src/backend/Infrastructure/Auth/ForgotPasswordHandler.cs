using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Email;

namespace MotsSupplierPortal.Infrastructure.Auth;

/// <summary>
/// FR-IAM-005: identical response whether or not the account exists (no enumeration).
/// If it exists, a single-use, time-limited reset token is queued via a durable email job.
/// SECURITY-ARCHITECTURE.md §1.7: the link carries only the opaque token, never the user id.
/// </summary>
public sealed class ForgotPasswordHandler(
    UserManager<AppUser> userManager,
    IBackgroundJobClient backgroundJobs) : IForgotPasswordHandler
{
    public async Task HandleAsync(ForgotPasswordCommand command, CancellationToken ct)
    {
        var user = await userManager.FindByEmailAsync(command.Email.Trim().ToLowerInvariant());
        if (user is null)
        {
            return; // silent no-op: caller sees the same "check your email" response either way
        }

        // The token is minted inside the job (MSP-89), not here. Baking it into a job argument
        // stored it in plaintext in the Hangfire tables for the whole retention window - a working
        // password-reset credential at rest, which combined with anonymous forgot-password was the
        // account-takeover chain MSP-87 found. The enumeration-resistance above is unchanged: the
        // caller still sees the same response whether or not the user exists.
        backgroundJobs.Enqueue<EmailJobs>(job => job.SendPasswordResetEmailAsync(user.Id, CancellationToken.None));
    }
}
