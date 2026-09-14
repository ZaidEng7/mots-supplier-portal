// Requesting a password reset link.
//
// The response is identical whether or not the account exists, so this is no oracle for which addresses are
// registered. An unknown address is a silent no-op.
//
// The link carries only the opaque single-use token, never a user identifier.
//
//
// THE TOKEN IS MINTED INSIDE THE JOB, NOT HERE
//
// Baking it into a job argument stored it in plain text in the job tables for the whole retention window: a
// working password-reset credential at rest, which together with an anonymous reset endpoint was an
// account-takeover chain a security review found.
//
// The resistance to enumeration above is unchanged, because the caller still sees the same response either way.

namespace MotsSupplierPortal.Infrastructure.Auth;

using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Email;

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

        backgroundJobs.Enqueue<EmailJobs>(job => job.SendPasswordResetEmailAsync(user.Id, CancellationToken.None));
    }
}
