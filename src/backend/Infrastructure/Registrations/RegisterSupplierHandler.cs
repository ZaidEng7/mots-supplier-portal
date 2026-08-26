using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Registrations;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Registrations;

/// <summary>
/// STORY-02.1.1: creates a Supplier (Draft) + supplier_admin User in one transaction.
/// No orphan records on failure. Duplicate email is a normalized, non-enumerating rejection
/// (STORY-02.3.1).
/// </summary>
public sealed class RegisterSupplierHandler(
    AppDbContext db,
    UserManager<AppUser> userManager,
    IAuditLogger auditLogger,
    IBackgroundJobClient backgroundJobs,
    IConfiguration configuration) : IRegisterSupplierHandler
{
    public async Task<RegisterSupplierResult> HandleAsync(RegisterSupplierCommand command, CancellationToken ct)
    {
        var normalizedEmail = command.Email.Trim().ToLowerInvariant();

        var existing = await userManager.FindByEmailAsync(normalizedEmail);
        if (existing is not null)
        {
            return new RegisterSupplierResult.DuplicateEmail();
        }

        IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var referenceCode = await ReferenceCodeGenerator.NextSupplierCodeAsync(db, ct);
            var supplier = Supplier.Register(
                referenceCode,
                command.DisplayNameAr,
                command.DisplayNameEn,
                command.RegistrationNumber,
                command.RepresentativeName,
                normalizedEmail);

            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync(ct);

            var user = new AppUser
            {
                Id = Guid.CreateVersion7(),
                UserName = normalizedEmail,
                Email = normalizedEmail,
                FullName = command.RepresentativeName,
                SupplierId = supplier.Id,
            };

            var createResult = await userManager.CreateAsync(user, command.Password);
            if (!createResult.Succeeded)
            {
                await transaction.RollbackAsync(ct);
                return new RegisterSupplierResult.WeakPassword(
                    [.. createResult.Errors.Select(e => e.Description)]);
            }

            await userManager.AddToRoleAsync(user, Roles.SupplierAdmin);

            var representative = supplier.Representatives[0];
            representative.UserId = user.Id;
            await db.SaveChangesAsync(ct);

            await transaction.CommitAsync(ct);

            // Verification email (STORY-02.2.1): queued on Hangfire as a durable job, not sent
            // inline. EPIC-15 (Notifications) owns the real SMTP/SES transport; IEmailSender is
            // stubbed to a logger until then, but the queuing/retry behavior is real.
            var confirmationToken = await userManager.GenerateEmailConfirmationTokenAsync(user);
            var encodedToken = WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(confirmationToken));
            var frontendUrl = configuration["App:PublicUrl"] ?? "http://localhost:5173";
            var verifyUrl = $"{frontendUrl}/verify-email?userId={user.Id}&token={Uri.EscapeDataString(encodedToken)}";

            backgroundJobs.Enqueue<EmailJobs>(job => job.SendVerificationEmailAsync(user.Email!, verifyUrl, CancellationToken.None));

            await auditLogger.LogAsync(
                aggregateType: "Supplier",
                aggregateId: supplier.Id,
                action: "register",
                correlationId: Guid.NewGuid(),
                actorUserId: user.Id,
                actorLabel: user.FullName,
                toState: nameof(SupplierOnboardingState.Draft),
                referenceCode: supplier.ReferenceCode,
                ct: ct);

            return new RegisterSupplierResult.Success(supplier.ReferenceCode);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }
}
