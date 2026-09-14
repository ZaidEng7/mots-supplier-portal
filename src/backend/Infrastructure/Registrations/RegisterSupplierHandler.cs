// A company registers itself: a supplier in draft and its administrator account, in one transaction.
//
// No orphan records on failure.
//
//
// THE RESPONSE CANNOT TELL A PROBER WHETHER AN ACCOUNT EXISTS
//
// This handler still returns distinct results for a duplicate email and a duplicate registration number, because
// which check fired is worth knowing internally.
//
// The endpoint maps all three of success and the two duplicates to the identical response shape, so a caller
// cannot learn which happened, or that anything happened at all, from the response alone.
//
// What replaces the leaked signal: the existing account is notified directly. A legitimate user who forgot they
// had already registered is helped, and a prober learns nothing.
//
//
// AND NEITHER CAN THE TIMING
//
// Measured directly against this handler: a genuine registration, with its transaction, account creation and
// audit row, averaged about sixty milliseconds, while a duplicate short-circuit averaged about five. An identical
// response body does not close a twelvefold gap.
//
// The floor is a best-effort constant-time measure rather than an exact one. It pads the FAST path up toward the
// slow path's typical cost rather than the reverse, because slowing down every genuine registration to match the
// rare duplicate case would be the wrong trade.
//
//
// THE DATABASE IS THE AUTHORITATIVE DUPLICATE CHECK, NOT THE PRE-CHECK
//
// The registration number is compared trimmed and case-sensitively; the migration records why it is not
// case-folded.
//
// The read before the insert is a fast path only. Two requests can both pass it before either commits, so the
// expression unique index is the guard. This codebase already learned that a check-then-insert has a window two
// concurrent requests can both pass, and the fix there was the same: make the database the source of truth rather
// than trust a read that happened moments earlier.
//
// The violation is matched on the specific index name, so an unrelated unique violation, a reference-code
// allocation for instance, is not silently mapped to the wrong result and swallowed.
//
// And the account to notify is looked up fresh in that handler rather than reused from the pre-check, because the
// pre-check found nothing or this path would never have been reached: the winning concurrent request committed
// after it ran.
//
//
// THE VERIFICATION EMAIL
//
// Queued as a durable job rather than sent inline. The transport is a stand-in until a real one lands, but the
// queuing and retry behaviour is real.
//
// The link carries only the opaque token and never a user identifier; the token alone resolves the user. The
// token is minted inside the job, because a verification link passed as a job argument sat in the job tables in
// plain text for the whole retention window.

namespace MotsSupplierPortal.Infrastructure.Registrations;

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

public sealed class RegisterSupplierHandler(
    AppDbContext db,
    UserManager<AppUser> userManager,
    IAuditLogger auditLogger,
    IBackgroundJobClient backgroundJobs) : IRegisterSupplierHandler
{
    private static readonly TimeSpan MinResponseTime = TimeSpan.FromMilliseconds(60);

    public async Task<RegisterSupplierResult> HandleAsync(RegisterSupplierCommand command, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalizedEmail = command.Email.Trim().ToLowerInvariant();

        var existing = await userManager.FindByEmailAsync(normalizedEmail);
        if (existing is not null)
        {
            backgroundJobs.Enqueue<EmailJobs>(job => job.SendAlreadyRegisteredNoticeEmailAsync(existing.Id, CancellationToken.None));
            await PadToMinimumResponseTimeAsync(stopwatch, ct);
            return new RegisterSupplierResult.DuplicateEmail();
        }

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
            await transaction.RollbackAsync(ct);
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
