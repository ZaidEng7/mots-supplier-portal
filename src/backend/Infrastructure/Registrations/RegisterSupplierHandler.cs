using System.Diagnostics;
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
/// <para><b>MSP-73, the enumeration fix referenced below.</b> This handler still returns distinct
/// <see cref="RegisterSupplierResult.DuplicateEmail"/>/<see cref="RegisterSupplierResult.DuplicateRegistrationNumber"/>
/// cases - that distinction is preserved for internal purposes (which duplicate check fired), but
/// the API endpoint (RegistrationEndpoints.cs) now maps ALL three of Success/DuplicateEmail/
/// DuplicateRegistrationNumber to the identical response shape, so a caller cannot learn which
/// happened, or that anything happened at all, from the response alone. What replaces the leaked
/// signal: the existing account is notified directly (NotifyExistingSupplierAsync /
/// EmailJobs.SendAlreadyRegisteredNoticeEmailAsync) - a legitimate user who forgot they'd already
/// registered is helped, and a prober learns nothing.</para>
/// </summary>
public sealed class RegisterSupplierHandler(
    AppDbContext db,
    UserManager<AppUser> userManager,
    IAuditLogger auditLogger,
    IBackgroundJobClient backgroundJobs) : IRegisterSupplierHandler
{
    // MSP-73: measured directly against this handler - a genuine registration (transaction,
    // Identity user creation, audit log) averaged ~62ms while a duplicate short-circuit averaged
    // ~5ms, a 12x gap. An identical response body doesn't close that: a prober can still learn
    // whether an email/registration number exists purely from how fast the response arrives.
    // This floor is a best-effort constant-time measure, not exact - it pads the FAST path up
    // toward the SLOW path's typical cost rather than the reverse (slowing down every genuine
    // registration to match the rare duplicate case would be the wrong trade).
    private static readonly TimeSpan MinResponseTime = TimeSpan.FromMilliseconds(60);

    public async Task<RegisterSupplierResult> HandleAsync(RegisterSupplierCommand command, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalizedEmail = command.Email.Trim().ToLowerInvariant();

        var existing = await userManager.FindByEmailAsync(normalizedEmail);
        if (existing is not null)
        {
            // MSP-73: notify the account that already owns this email, not the submitter - a
            // legitimate user who forgot they'd registered is still helped, and nothing is sent
            // back in the API response that would tell a prober the email exists (see
            // RegistrationEndpoints.cs's identical-response mapping).
            backgroundJobs.Enqueue<EmailJobs>(job => job.SendAlreadyRegisteredNoticeEmailAsync(existing.Id, CancellationToken.None));
            await PadToMinimumResponseTimeAsync(stopwatch, ct);
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
            var existingSupplierId = await FindSupplierIdByRegistrationNumberAsync(normalizedRegistrationNumber, ct);
            if (existingSupplierId is not null)
            {
                await NotifyExistingSupplierAsync(existingSupplierId.Value, ct);
                await PadToMinimumResponseTimeAsync(stopwatch, ct);
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
                Language = command.Locale,
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
            // The race the pre-check can't close (MSP-81): the winning concurrent request already
            // committed by the time this one's insert violates the unique index, so the row to
            // notify has to be looked up fresh here rather than reused from the pre-check (which
            // found nothing, or this catch would never have been reached).
            if (!string.IsNullOrEmpty(normalizedRegistrationNumber))
            {
                var winnerSupplierId = await FindSupplierIdByRegistrationNumberAsync(normalizedRegistrationNumber, ct);
                if (winnerSupplierId is not null)
                {
                    await NotifyExistingSupplierAsync(winnerSupplierId.Value, ct);
                }
            }
            await PadToMinimumResponseTimeAsync(stopwatch, ct);
            return new RegisterSupplierResult.DuplicateRegistrationNumber();
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private static Task PadToMinimumResponseTimeAsync(Stopwatch stopwatch, CancellationToken ct)
    {
        var remaining = MinResponseTime - stopwatch.Elapsed;
        return remaining > TimeSpan.Zero ? Task.Delay(remaining, ct) : Task.CompletedTask;
    }

    private Task<Guid?> FindSupplierIdByRegistrationNumberAsync(string normalizedRegistrationNumber, CancellationToken ct) =>
        db.Suppliers
            .Where(s => s.LegalInfo != null && s.LegalInfo.RegistrationNumber != null
                && s.LegalInfo.RegistrationNumber.Trim() == normalizedRegistrationNumber)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(ct);

    /// <summary>MSP-73: resolves the existing supplier's own primary user rather than the
    /// submitter's - same lookup shape as ReviewerNotify.GetPrimaryUserIdAsync in
    /// ReviewApplicationHandlers.cs.</summary>
    private async Task NotifyExistingSupplierAsync(Guid supplierId, CancellationToken ct)
    {
        var primaryUserId = await db.Users
            .Where(u => u.SupplierId == supplierId)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct);
        if (primaryUserId is not null)
        {
            backgroundJobs.Enqueue<EmailJobs>(job => job.SendAlreadyRegisteredNoticeEmailAsync(primaryUserId.Value, CancellationToken.None));
        }
    }

    private static bool IsRegistrationNumberUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" } pg
        && pg.ConstraintName == "IX_supplier_RegistrationNumber_Normalized";
}
