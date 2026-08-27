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
    ISecurityTokenService securityTokenService,
    IBackgroundJobClient backgroundJobs,
    IConfiguration configuration) : IResendVerificationHandler
{
    public async Task HandleAsync(ResendVerificationCommand command, CancellationToken ct)
    {
        var user = await userManager.FindByEmailAsync(command.Email.Trim().ToLowerInvariant());
        if (user is null || user.EmailConfirmed)
        {
            return;
        }

        var rawToken = await securityTokenService.IssueAsync(user.Id, SecurityTokenPurpose.EmailVerification, TimeSpan.FromHours(24), ct);
        var frontendUrl = configuration["App:PublicUrl"] ?? "http://localhost:5173";
        var verifyUrl = $"{frontendUrl}/verify-email?token={Uri.EscapeDataString(rawToken)}";

        backgroundJobs.Enqueue<EmailJobs>(job => job.SendVerificationEmailAsync(user.Email!, verifyUrl, CancellationToken.None));
    }
}
