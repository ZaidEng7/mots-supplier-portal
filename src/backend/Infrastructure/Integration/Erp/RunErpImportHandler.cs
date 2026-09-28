// Creating portal suppliers and their accounts from the ERP.
//
// ONE TRANSACTION PER SUPPLIER, NOT ONE FOR THE RUN. Eighty suppliers means eighty chances to hit something
// nobody anticipated, and a single transaction would throw away seventy-nine good rows because of the eightieth.
// It also means a re-run after a fix has less to redo, which matters because the first real run will be re-run.
//
// A FAILED SUPPLIER IS FORGOTTEN BEFORE THE NEXT ONE. The run shares one database context, and a supplier whose save
// failed stays in it, waiting to be saved again - so without clearing it every later supplier's save retried the
// failed one and failed with it. One value too long for its column emptied the rest of the run.
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
// EVERY SUPPLIER IS ADMITTED. Gaps are filled - a placeholder email, an empty currency, suspension for a supplier
// the ERP has disabled - by ErpImportAdmission, which the preview also uses, and every filled gap is written into
// the row's notes. The one refusal left is an address that already belongs to another account, because two
// suppliers cannot share one login.
//
// REFUSALS AND FAILURES ARE STILL COUNTED SEPARATELY. A refusal is a supplier the portal declined for a stated
// reason; a failure is the import going wrong. One number for both would hide a defect inside an expected result.
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

        var registrationNumbersInPortal = await RegistrationNumbersInPortal.ReadAsync(db, ct);
        var erpSuppliers = await source.ListSuppliersAsync(ct);
        var registrationNumbers = ErpRegistrationNumbers.Decide(erpSuppliers, registrationNumbersInPortal);
        var rows = new List<ErpImportResultRow>();

        foreach (var erpSupplier in erpSuppliers)
        {
            rows.Add(await ImportOneAsync(erpSupplier, registrationNumbers[erpSupplier.ExternalId], password, ct));
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
        ErpSupplier erpSupplier, ErpRegistrationNumberDecision registrationNumber, string password, CancellationToken ct)
    {
        var admitted = ErpImportAdmission.Admit(erpSupplier);
        if (registrationNumber.Note is not null)
        {
            admitted = admitted with { Notes = [.. admitted.Notes, registrationNumber.Note] };
        }

        var details = new ErpSupplierDetails(
            admitted.ArabicName,
            registrationNumber.Number,
            admitted.RegistrationType,
            admitted.SupplierGroup,
            admitted.Description);

        try
        {
            var existing = await db.Suppliers
                .Include(s => s.Representatives)
                .Include(s => s.Addresses)
                .FirstOrDefaultAsync(s => s.ExternalId == erpSupplier.ExternalId, ct);

            return existing is null
                ? await CreateAsync(erpSupplier, admitted, details, password, ct)
                : await UpdateAsync(existing, erpSupplier, admitted, details, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Importing {Supplier} failed.", erpSupplier.ExternalId);
            db.ChangeTracker.Clear();

            return new ErpImportResultRow(
                erpSupplier.ExternalId, admitted.Name, ErpImportOutcome.Failed, null, [exception.Message]);
        }
    }

    private async Task<ErpImportResultRow> CreateAsync(
        ErpSupplier erpSupplier,
        AdmittedSupplier admitted,
        ErpSupplierDetails details,
        string password,
        CancellationToken ct)
    {
        var taken = await userManager.FindByEmailAsync(admitted.Email);
        if (taken is not null)
        {
            return new ErpImportResultRow(
                erpSupplier.ExternalId,
                admitted.Name,
                ErpImportOutcome.Refused,
                null,
                [$"The address {admitted.Email} already belongs to another account in the portal.", .. admitted.Notes]);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var referenceCode = await ReferenceCodeGenerator.NextSupplierCodeAsync(db, ct);

        var supplier = Supplier.ImportFromErp(
            referenceCode,
            erpSupplier.ExternalId,
            admitted.Name,
            erpSupplier.TaxId,
            LegalTypeOf(erpSupplier.LegalType),
            admitted.Currency,
            admitted.RepresentativeName,
            admitted.Email,
            erpSupplier.Phone,
            admitted.Suspended,
            details);

        var address = ErpImportAdmission.AddressOutcome(admitted, isNew: true, addressesInPortal: 0, blockedByState: null);
        if (address.Write) AddAddress(supplier, admitted.Address!.Address);

        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync(ct);

        var user = new AppUser
        {
            Id = Guid.CreateVersion7(),
            UserName = admitted.Email,
            Email = admitted.Email,
            FullName = admitted.RepresentativeName,
            SupplierId = supplier.Id,
            EmailConfirmed = true,
        };

        var created = await userManager.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();

            return new ErpImportResultRow(
                erpSupplier.ExternalId,
                admitted.Name,
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
            admitted.Name,
            ErpImportOutcome.Created,
            referenceCode,
            [$"Approved without portal review, imported from the ERP. Account created for {admitted.Email}.",
             .. admitted.Notes,
             address.Note]);
    }

    // Updating a supplier the portal already holds.
    //
    // A PLACEHOLDER NEVER OVERWRITES A REAL ADDRESS. If the ERP still has no email, the one on file stays - it may
    // be a real address the supplier gave the portal since.
    //
    // A REAL ADDRESS REPLACES A PLACEHOLDER ON THE LOGIN TOO, not only on the contact record. The account was made
    // on the placeholder, and a contact updated while the login stayed on the .invalid address would leave the
    // supplier's actual person unable to sign in with the address everyone now has on file. Only a placeholder
    // login is moved: an account somebody has already changed to a real address is theirs.
    //
    // THE ADDRESS IS ADDED ONLY TO A SUPPLIER THAT HAS NONE. Once there is an address the supplier or the ministry may
    // have corrected it - placed the pin, fixed the governorate the ERP's city field got wrong - and the ERP's version
    // is the older truth. Every other field the ERP sends is refreshed, and nothing it lacks is blanked.
    private async Task<ErpImportResultRow> UpdateAsync(
        Supplier existing,
        ErpSupplier erpSupplier,
        AdmittedSupplier admitted,
        ErpSupplierDetails details,
        CancellationToken ct)
    {
        var notes = new List<string>
        {
            "Updated from the ERP. Documents, bank details and anything else the portal holds were left alone.",
        };

        var realEmail = admitted.EmailIsPlaceholder ? null : admitted.Email;

        existing.ApplyErpSnapshot(
            admitted.Name,
            erpSupplier.TaxId,
            LegalTypeOf(erpSupplier.LegalType),
            admitted.Currency,
            realEmail,
            erpSupplier.Phone,
            admitted.Suspended,
            details,
            admitted.ContactPerson);

        var address = ErpImportAdmission.AddressOutcome(
            admitted,
            isNew: false,
            existing.Addresses.Count,
            Supplier.AllowsContactEdits(existing.OnboardingState) ? null : $"in state '{existing.OnboardingState}'");
        if (address.Write) AddAddress(existing, admitted.Address!.Address);
        notes.Add(address.Note);

        if (realEmail is not null)
        {
            var moved = await MoveLoginOffPlaceholderAsync(existing, realEmail);
            if (moved is not null) notes.Add(moved);
        }

        existing.MarkSynced(erpSupplier.ExternalId);
        await db.SaveChangesAsync(ct);

        notes.AddRange(admitted.Notes);

        return new ErpImportResultRow(
            erpSupplier.ExternalId,
            admitted.Name,
            ErpImportOutcome.Updated,
            existing.ReferenceCode,
            notes);
    }

    // The new address is added to the context explicitly, as ManageAddressHandler does. Its identifier is set by the
    // domain, so on a supplier that is already tracked the change tracker takes it for an existing row and issues an
    // update that touches nothing - the save then fails as a concurrency conflict, and the supplier with it.
    private void AddAddress(Supplier supplier, MappedErpAddress? address)
    {
        if (address is null) return;

        db.Addresses.Add(supplier.AddAddress(
            AddressKind.Billing,
            address.Line1,
            address.Line2,
            address.City,
            address.RegionCode,
            address.Country,
            postalCode: null,
            latitude: null,
            longitude: null));
    }

    private async Task<string?> MoveLoginOffPlaceholderAsync(Supplier supplier, string realEmail)
    {
        var userId = supplier.Representatives.FirstOrDefault(r => r.IsPrimary)?.UserId
            ?? supplier.Representatives.FirstOrDefault()?.UserId;
        if (userId is null) return null;

        var user = await userManager.FindByIdAsync(userId.Value.ToString());
        if (user is null || !ErpImportAdmission.IsPlaceholder(user.Email)) return null;

        if (await userManager.FindByEmailAsync(realEmail) is not null)
        {
            return $"The ERP now has {realEmail}, but another account already uses it; the login stays on the placeholder.";
        }

        var previous = user.Email;

        await userManager.SetEmailAsync(user, realEmail);
        await userManager.SetUserNameAsync(user, realEmail);

        user.EmailConfirmed = true;
        await userManager.UpdateAsync(user);

        return $"The login moved from placeholder {previous} to {realEmail}.";
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
