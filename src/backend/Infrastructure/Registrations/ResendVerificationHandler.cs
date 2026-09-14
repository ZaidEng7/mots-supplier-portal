// Sending the verification email again.
//
// Rate-limited at the endpoint, per address and per caller, and like the forgot-password path it never reveals
// whether the address exists or is already verified.
//
// The token is minted inside the job, for the reason the forgot-password handler explains.

namespace MotsSupplierPortal.Infrastructure.Registrations;

using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Registrations;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Email;

public sealed class ResendVerificationHandler(
    UserManager<AppUser> userManager,
    IBackgroundJobClient backgroundJobs) : IResendVerificationHandler
{
    public async Task HandleAsync(ResendVerificationCommand command, CancellationToken ct)
    {
        var user = await userManager.FindByEmailAsync(command.Email.Trim().ToLowerInvariant());
        if (user is null || user.EmailConfirmed)
        {
            return;
        }

        backgroundJobs.Enqueue<EmailJobs>(job => job.SendVerificationEmailAsync(user.Id, CancellationToken.None));
    }
}
