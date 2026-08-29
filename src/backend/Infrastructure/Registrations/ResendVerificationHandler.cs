using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Registrations;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Email;

namespace MotsSupplierPortal.Infrastructure.Registrations;

/// <summary>STORY-02.2.1 AC3: resend is rate-limited at the endpoint (per-IP + per-target) and,
/// like ForgotPasswordHandler, never reveals whether the address exists or is already verified.</summary>
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

        // Token minted inside the job (MSP-89) - see ForgotPasswordHandler for why.
        backgroundJobs.Enqueue<EmailJobs>(job => job.SendVerificationEmailAsync(user.Id, CancellationToken.None));
    }
}
