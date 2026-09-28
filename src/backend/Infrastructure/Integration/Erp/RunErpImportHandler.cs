// Creating portal suppliers and their accounts from the ERP.
//
// ONE TRANSACTION PER SUPPLIER, NOT ONE FOR THE RUN. Eighty suppliers means eighty chances to hit something
// nobody anticipated, and a single transaction would throw away seventy-nine good rows because of the eightieth.
// It also means a re-run after a fix has less to redo, which matters because the first real run will be re-run.
//
// THE PASSWORD IS CHECKED ONCE, BEFORE ANYTHING IS WRITTEN. The identity framework enforces twelve characters and
// refuses passwords found in public breaches, and discovering that on the first account leaves a supplier row
// with no account attached and seventy-nine to go. So a throwaway validation runs first and the whole run stops
// with one clear message instead.
//
// THE ACCOUNT IS CREATED ALREADY CONFIRMED, and that is not a shortcut. Sign-in refuses an unconfirmed address -
// LoginHandler checks EmailConfirmed before anything else - so an imported account that was not confirmed could
// never be used at all, and the shared password would be useless. Nobody clicked a link because nobody was sent
// one; the ministry is asserting these addresses, not verifying them, and that is what this records.
//
// NO EMAIL IS SENT. That was asked for, and the reason to write it down is that the invitation path still exists
// and is the better answer for the real registry: eighty real companies receiving an unexpected email from the
// ministry is not a thing to do by accident, and the shared password this replaces it with is discussed in
// ErpImportOptions.
//
// A SUPPLIER ALREADY CARRYING THIS ERP'S IDENTIFIER IS UPDATED, NEVER DUPLICATED, and its account is left alone -
// no second user, no password reset. Pressing this button twice is a thing that will happen.
//
// REFUSALS AND FAILURES ARE COUNTED SEPARATELY. A refusal is this product declining a supplier it cannot
// represent; a failure is the import going wrong. One number for both would hide a defect inside an expected
// result.
//
// THE AUDIT ROW IS WRITTEN AND SAVED BEFORE THE FIRST SUPPLIER IS TOUCHED. Three export routes in this product
// logged without saving for months and wrote nothing at all; the shape of that bug was a LogAsync with no
// SaveChangesAsync after it.

namespace MotsSupplierPortal.Infrastructure.Integration.Erp;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Integration;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Registrations;

public sealed class RunErpImportHandler(
    IErpSupplierSource source,
    AppDbContext db,
    UserManager<AppUser> userManager,
    IOptions<ErpImportOptions> options,
    IAuditLogger audit,
    ILogger<RunErpImportHandler> logger) : IRunErpImportHandler
{
    public async Task<ErpImportRunReport> HandleAsync(CancellationToken ct)
    {
        await audit.LogAsync(
            aggregateType: "Supplier",
            aggregateId: Guid.Empty,
            action: "ErpImportRun",
            ct: ct);

        await db.SaveChangesAsync(ct);

        var password = options.Value.InitialPassword;
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ErpImportNotConfiguredException(
                "No initial password is configured: set ErpImport:InitialPassword.");
        }

        await EnsurePasswordIsAcceptableAsync(password);

        var erpSuppliers = await source.ListSuppliersAsync(ct);
        var rows = new List<ErpImportResultRow>();

        foreach (var erpSupplier in erpSuppliers)
        {
            rows.Add(await ImportOneAsync(erpSupplier, password, ct));
        }

        logger.LogInformation(
            "ERP import finished: {Created} created, {Updated} updated, {Refused} refused, {Failed} failed.",
            rows.Count(r => r.Outcome == ErpImportOutcome.Created),
            rows.Count(r => r.Outcome == ErpImportOutcome.Updated),
            rows.Count(r => r.Outcome == ErpImportOutcome.Refused),
            rows.Count(r => r.Outcome == ErpImportOutcome.Failed));

        return new ErpImportRunReport(
            erpSuppliers.Count,
            rows.Count(r => r.Outcome == ErpImportOutcome.Created),
            rows.Count(r => r.Outcome == ErpImportOutcome.Updated),
            rows.Count(r => r.Outcome == ErpImportOutcome.Refused),
            rows.Count(r => r.Outcome == ErpImportOutcome.Failed),
            rows);
    }

    private async Task<ErpImportResultRow> ImportOneAsync(
        ErpSupplier erpSupplier, string password, CancellationToken ct)
    {
        var refusals = ErpImportAdmission.Refusals(erpSupplier);
        if (refusals.Count > 0)
        {
            return new ErpImportResultRow(
                erpSupplier.ExternalId, erpSupplier.Name, ErpImportOutcome.Refused, null, refusals);
        }

        try
        {
            var existing = await db.Suppliers
                .Include(s => s.Representatives)
                .FirstOrDefaultAsync(s => s.ExternalId == erpSupplier.ExternalId, ct);

            return existing is null
                ? await CreateAsync(erpSupplier, password, ct)
                : await UpdateAsync(existing, erpSupplier, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Importing {Supplier} failed.", erpSupplier.ExternalId);

            return new ErpImportResultRow(
                erpSupplier.ExternalId, erpSupplier.Name, ErpImportOutcome.Failed, null, [exception.Message]);
        }
    }

    private async Task<ErpImportResultRow> CreateAsync(
        ErpSupplier erpSupplier, string password, CancellationToken ct)
    {
        var email = erpSupplier.Email!.Trim().ToLowerInvariant();

        var taken = await userManager.FindByEmailAsync(email);
        if (taken is not null)
        {
            return new ErpImportResultRow(
                erpSupplier.ExternalId,
                erpSupplier.Name,
                ErpImportOutcome.Refused,
                null,
                [$"The address {email} already belongs to another account in the portal."]);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var referenceCode = await ReferenceCodeGenerator.NextSupplierCodeAsync(db, ct);

        var supplier = Supplier.ImportFromErp(
            referenceCode,
            erpSupplier.ExternalId,
            erpSupplier.Name!,
            erpSupplier.TaxId,
            LegalTypeOf(erpSupplier.LegalType),
            erpSupplier.Currency,
            erpSupplier.Name!,
            email,
            erpSupplier.Phone);

        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync(ct);

        var user = new AppUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            FullName = erpSupplier.Name!,
            SupplierId = supplier.Id,
            EmailConfirmed = true,
        };

        var created = await userManager.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            await transaction.RollbackAsync(ct);

            return new ErpImportResultRow(
                erpSupplier.ExternalId,
                erpSupplier.Name,
                ErpImportOutcome.Failed,
                null,
                [.. created.Errors.Select(error => error.Description)]);
        }

        await userManager.AddToRoleAsync(user, Roles.SupplierAdmin);

        supplier.Representatives[0].UserId = user.Id;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new ErpImportResultRow(
            erpSupplier.ExternalId,
            erpSupplier.Name,
            ErpImportOutcome.Created,
            referenceCode,
            [$"Approved without portal review, imported from the ERP. Account created for {email}."]);
    }

    private async Task<ErpImportResultRow> UpdateAsync(
        Supplier existing, ErpSupplier erpSupplier, CancellationToken ct)
    {
        existing.ApplyErpSnapshot(
            erpSupplier.Name!,
            erpSupplier.TaxId,
            LegalTypeOf(erpSupplier.LegalType),
            erpSupplier.Currency,
            erpSupplier.Email!.Trim().ToLowerInvariant(),
            erpSupplier.Phone);

        existing.MarkSynced(erpSupplier.ExternalId);
        await db.SaveChangesAsync(ct);

        return new ErpImportResultRow(
            erpSupplier.ExternalId,
            erpSupplier.Name,
            ErpImportOutcome.Updated,
            existing.ReferenceCode,
            ["Updated from the ERP. The account, documents and anything the portal holds were left alone."]);
    }

    // A throwaway account is never saved: CreateAsync is not called, only the validators are, so nothing reaches
    // the database. It is the only way to ask the identity framework "would you accept this password" without
    // duplicating its rules here - and duplicating them is how the copy drifts from the real one.
    private async Task EnsurePasswordIsAcceptableAsync(string password)
    {
        var probe = new AppUser
        {
            Id = Guid.CreateVersion7(),
            UserName = "erp-import-probe",
            Email = "probe@invalid",
            FullName = "ERP import probe",
        };

        foreach (var validator in userManager.PasswordValidators)
        {
            var result = await validator.ValidateAsync(userManager, probe, password);
            if (result.Succeeded) continue;

            throw new ErpImportNotConfiguredException(
                "The configured import password is refused: "
                + string.Join(" ", result.Errors.Select(error => error.Description)));
        }
    }

    private static SupplierLegalType LegalTypeOf(string? erpType) => erpType switch
    {
        "Individual" => SupplierLegalType.Individual,
        _ => SupplierLegalType.Company,
    };
}

public sealed class ErpImportNotConfiguredException(string message) : Exception(message);
