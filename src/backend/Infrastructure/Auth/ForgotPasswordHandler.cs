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
    ISecurityTokenService securityTokenService,
    IBackgroundJobClient backgroundJobs,
    IConfiguration configuration) : IForgotPasswordHandler
{
    public async Task HandleAsync(ForgotPasswordCommand command, CancellationToken ct)
    {
        var user = await userManager.FindByEmailAsync(command.Email.Trim().ToLowerInvariant());
        if (user is null)
        {
            return; // silent no-op: caller sees the same "check your email" response either way
        }

        var rawToken = await securityTokenService.IssueAsync(user.Id, SecurityTokenPurpose.PasswordReset, TimeSpan.FromMinutes(30), ct);
        var frontendUrl = configuration["App:PublicUrl"]
            ?? throw new InvalidOperationException("App:PublicUrl is not configured.");
        var resetUrl = $"{frontendUrl}/reset-password?token={Uri.EscapeDataString(rawToken)}";

        backgroundJobs.Enqueue<EmailJobs>(job => job.SendPasswordResetEmailAsync(user.Email!, resetUrl, CancellationToken.None));
    }
}
