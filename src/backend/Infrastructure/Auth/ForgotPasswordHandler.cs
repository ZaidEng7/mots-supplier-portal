using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Email;

namespace MotsSupplierPortal.Infrastructure.Auth;

/// <summary>
/// FR-IAM-005: identical response whether or not the account exists (no enumeration).
/// If it exists, a single-use, time-limited reset token is queued via a durable email job.
/// </summary>
public sealed class ForgotPasswordHandler(
    UserManager<AppUser> userManager,
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

        var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
        var encodedToken = WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(resetToken));
        var frontendUrl = configuration["App:PublicUrl"] ?? "http://localhost:5173";
        var resetUrl = $"{frontendUrl}/reset-password?userId={user.Id}&token={Uri.EscapeDataString(encodedToken)}";

        backgroundJobs.Enqueue<EmailJobs>(job => job.SendPasswordResetEmailAsync(user.Email!, resetUrl, CancellationToken.None));
    }
}
