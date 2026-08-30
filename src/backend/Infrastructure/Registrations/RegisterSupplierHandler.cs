using Hangfire;
using Microsoft.AspNetCore.Identity;
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
/// No orphan records on failure.
///
/// <para><b>Correction to what this comment used to claim.</b> It previously said duplicate email
/// was "a normalized, non-enumerating rejection". Normalized, yes. Non-enumerating, no: the API
/// maps <see cref="RegisterSupplierResult.DuplicateEmail"/> to a distinct 409
/// (<c>Results.Conflict</c>) against a 201 on success, which lets a caller learn whether an email
/// is registered without ever verifying it. That is live today, confirmed by reading the endpoint
/// mapping while adding <see cref="RegisterSupplierResult.DuplicateRegistrationNumber"/> below -
/// not fixed here, because whether either dedupe response should stop naming the reason is the
/// enumeration-fix's decision (task #17 / MSP-73), not this ticket's. Recorded rather than left to
/// contradict the code the next time someone reads this comment before the endpoint.</para>
/// </summary>
public sealed class RegisterSupplierHandler(
    AppDbContext db,
    UserManager<AppUser> userManager,
    IAuditLogger auditLogger,
    IBackgroundJobClient backgroundJobs) : IRegisterSupplierHandler
{
    public async Task<RegisterSupplierResult> HandleAsync(RegisterSupplierCommand command, CancellationToken ct)
    {
        var normalizedEmail = command.Email.Trim().ToLowerInvariant();

        var existing = await userManager.FindByEmailAsync(normalizedEmail);
        if (existing is not null)
        {
            return new RegisterSupplierResult.DuplicateEmail();
        }

        // FR-REG-004: whitespace-trimmed, case-sensitive - see the migration for why not
        // case-folded. This pre-check is a fast path only; the database's expression unique index
        // is the authoritative guard. MSP-81 already taught this codebase that a check-then-insert
        // has a window two concurrent requests can both pass, and the fix there was to make the
        // database the source of truth rather than trust a read that happened moments earlier.
        var normalizedRegistrationNumber = command.RegistrationNumber?.Trim();
        if (!string.IsNullOrEmpty(normalizedRegistrationNumber))
        {
            var duplicateRegistrationNumber = await db.Suppliers
                .AnyAsync(s => s.LegalInfo != null && s.LegalInfo.RegistrationNumber != null
                    && s.LegalInfo.RegistrationNumber.Trim() == normalizedRegistrationNumber, ct);

            if (duplicateRegistrationNumber)
            {
                return new RegisterSupplierResult.DuplicateRegistrationNumber();
            }
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
                normalizedEmail,
                command.RepresentativePhone);

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
            // SECURITY-ARCHITECTURE.md §1.6: the link carries only the opaque token, never the
            // user id - the token alone resolves the user (see SecurityTokenService).
            // Token minted inside the job (MSP-89): a verification URL passed as a job argument sat
            // in the Hangfire tables in plaintext for the whole retention window.
            backgroundJobs.Enqueue<EmailJobs>(job => job.SendVerificationEmailAsync(user.Id, CancellationToken.None));

            await auditLogger.LogAsync(
                aggregateType: "Supplier",
                aggregateId: supplier.Id,
                action: "register",
                actorUserId: user.Id,
                actorLabel: user.FullName,
                toState: nameof(SupplierOnboardingState.Draft),
                referenceCode: supplier.ReferenceCode,
                ct: ct);

            return new RegisterSupplierResult.Success(supplier.ReferenceCode);
        }
        catch (DbUpdateException ex) when (IsRegistrationNumberUniqueViolation(ex))
        {
            // The authoritative guard, not the pre-check above. Two requests can both pass the
            // AnyAsync check before either commits - the pre-check narrows the window, it does not
            // close it. Matched on the specific index name so an unrelated unique-violation (e.g.
            // reference-code allocation) is not silently mapped to the wrong result and swallowed.
            await transaction.RollbackAsync(ct);
            return new RegisterSupplierResult.DuplicateRegistrationNumber();
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private static bool IsRegistrationNumberUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" } pg
        && pg.ConstraintName == "IX_supplier_RegistrationNumber_Normalized";
}
