// The supplier's own profile: reading it, editing it, its logo, its representatives, addresses, contacts,
// branches, bank accounts and categories, accepting the terms, and submitting for review.
//
// Everything here is the caller's own company, resolved from their token rather than from a path parameter,
// so the interface never needs to know its own reference code to drive registration. Where a code does appear
// in the path it is checked against that scope first, and an unknown code and somebody else's code give the
// same not-found answer.
//
// Reading the profile needs no particular permission beyond being signed in, because any of a supplier's
// users may look up their own company's record.
//
//
// A TRUE PARTIAL UPDATE
//
// Every field on the profile patch is wrapped, so omitting one leaves it untouched while sending it as
// nothing clears it.
//
// They used to be plain nullable fields, which made an omitted field indistinguishable from an explicit
// clear, and silently wiped anything the caller did not resend. Found in review.
//
// Only what was actually sent is validated, because an omitted currency is not an invalid currency.
//
//
// VALIDATION WORTH KNOWING ABOUT
//
// Which legal-information fields are required is configuration rather than code, so a ministry can change it.
// Length limits stay fixed, because those are column widths; only requiredness is configurable.
//
// Those configurable failures are raised with the validation library's own standard code rather than a bare
// message, so the message catalogue resolves them to Arabic exactly as a declared rule would. Without the
// code they would fall through to English.
//
// A founding date in the future is refused. No company has one. The screen offered one, because the date
// picker's calendar ran into next month, and this validation accepted it, so the registry could show a
// supplier founded after today. It is refused here as well as in the form, because the form is one of several
// ways in and the finance-system import is another.
//
// A bank account's number is optional on an update, where sending nothing leaves the stored encrypted value
// untouched.
//
//
// THE STALE-WRITE ANSWER
//
// A lost update is a failed precondition rather than a conflict. This route used to answer conflict with the
// current version in the body, which predates the current contract; conflict now means only what the
// contract says it means. The winner's version travels back on the header rather than in the body, because
// that is where a client looking to re-read and retry is already looking.
//
// A field the caller is not currently permitted to edit is refused rather than answered as a conflict,
// because that is a permission outcome rather than a clash of state.
//
//
// WRITE PRECONDITIONS ON THE CHILD ROUTES
//
// Every route that edits a child of the profile, from representatives through to category links, requires the
// caller to say which version they read.
//
// They all moved the profile's version while guarding nothing, so two of a company's users editing different
// contacts both won, and the second silently overwrote something invisible.
//
// It is safe to require here because the precondition is obtainable: the profile read already issues the
// version as a tag. That check is the rule this project learned the hard way on another resource, where a
// guarded write had no read that could supply its precondition and therefore refused every caller.
//
// The bank-account reveal does not carry it, because it is a read and a read has nothing to lose to a
// concurrent write.
//
// The logo upload and accepting the terms do carry it. The logo is a field of the profile, and the version
// moved on every upload while guarding nothing, so two users uploading different logos both won and the
// second silently replaced the first. Accepting the terms records a version and a timestamp and gates
// submission, so a stale caller re-accepting an older version of the terms over a newer acceptance is the one
// lost update here with a compliance consequence rather than a cosmetic one.
//
//
// THE ONE ROUTE THAT WAS MISSING ITS RESPONSE VERSION
//
// Out of twenty-three writes on this profile, one changed the supplier and answered with no version at all,
// so every client kept asserting the version it read before that write, and the next guarded save on the same
// page was refused with nothing on screen to explain it.
//
// Found by filling in registration as a supplier: save the legal details, choose a currency, save again, and
// the currency is silently gone after a reload.
//
//
// OTHER NOTES
//
// The logo was a dead field for a while: the domain could set one and nothing called it.
//
// Bank accounts are gated on their own narrower permission, held by a supplier's administrator only, never on
// the general profile-edit permission.
//
// Accepting the terms is recorded with its version and a timestamp, and it gates submission alongside profile
// completeness and the required documents.

namespace MotsSupplierPortal.Api.Endpoints;

using MotsSupplierPortal.Api.Concurrency;
using FluentValidation.Results;
using MotsSupplierPortal.Api.Errors;
using FluentValidation;
using MotsSupplierPortal.Application.Common;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed record UpdateProfileRequest(
    Patch<string?> Description,
    Patch<string?> Website,
    Patch<string?> SupplierGroup,
    Patch<string?> CurrencyCode,
    Patch<string?> PrimaryContactPhone);

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator(AppDbContext db)
    {
        RuleFor(x => x.CurrencyCode.Value).Length(3)
            .When(x => x.CurrencyCode.IsSet && x.CurrencyCode.Value is not null);
        RuleFor(x => x.CurrencyCode.Value)
            .MustAsync(async (code, ct) => await db.Currencies.AnyAsync(c => c.Code == code && c.IsActive, ct))
            .WithMessage("Unknown or inactive currency code.")
            .When(x => x.CurrencyCode.IsSet && x.CurrencyCode.Value is not null);
    }
}

public sealed record UpdateLegalInfoRequest(string LegalNameAr, string LegalNameEn, string? RegistrationNumber, string? TaxId, SupplierLegalType SupplierType, DateOnly? EstablishedOn);

public sealed class UpdateLegalInfoRequestValidator : AbstractValidator<UpdateLegalInfoRequest>
{
    public UpdateLegalInfoRequestValidator(AppDbContext db)
    {
        RuleFor(x => x.LegalNameAr).MaximumLength(200);
        RuleFor(x => x.LegalNameEn).MaximumLength(200);
        RuleFor(x => x.RegistrationNumber).MaximumLength(100);
        RuleFor(x => x.TaxId).MaximumLength(100);

        RuleFor(x => x).CustomAsync(async (request, context, ct) =>
        {
            var required = await db.Set<SupplierFieldConfig>()
                .Where(c => c.Category == FieldConfigCategory.LegalInfoRequired && c.IsEnabled)
                .Select(c => c.FieldCode)
                .ToListAsync(ct);

            if (required.Contains("legalNameAr") && string.IsNullOrWhiteSpace(request.LegalNameAr))
                context.AddFailure(new ValidationFailure(nameof(request.LegalNameAr), "'Legal Name Ar' must not be empty.") { ErrorCode = "NotEmptyValidator" });
            if (required.Contains("legalNameEn") && string.IsNullOrWhiteSpace(request.LegalNameEn))
                context.AddFailure(new ValidationFailure(nameof(request.LegalNameEn), "'Legal Name En' must not be empty.") { ErrorCode = "NotEmptyValidator" });
            if (required.Contains("registrationNumber") && string.IsNullOrWhiteSpace(request.RegistrationNumber))
                context.AddFailure(new ValidationFailure(nameof(request.RegistrationNumber), "'Registration Number' must not be empty.") { ErrorCode = "NotEmptyValidator" });
            if (required.Contains("taxId") && string.IsNullOrWhiteSpace(request.TaxId))
                context.AddFailure(new ValidationFailure(nameof(request.TaxId), "'Tax Id' must not be empty.") { ErrorCode = "NotEmptyValidator" });
if (required.Contains("establishedOn") && request.EstablishedOn is null)
                context.AddFailure(nameof(request.EstablishedOn), "'Established On' must not be empty.");

            if (request.EstablishedOn is { } founded && founded > DateOnly.FromDateTime(DateTime.UtcNow.Date))
                context.AddFailure(nameof(request.EstablishedOn), "'Established On' cannot be in the future.");
        });
    }
}

public sealed record AddRepresentativeRequest(string FullName, string Email, string? Phone, string? Position);

public sealed class AddRepresentativeRequestValidator : AbstractValidator<AddRepresentativeRequest>
{
    public AddRepresentativeRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
    }
}

public sealed record AddAddressRequest(AddressKind Kind, string Line1, string? Line2, string City, string RegionCode, string Country, string? PostalCode, double? Latitude, double? Longitude);

public sealed class AddAddressRequestValidator : AbstractValidator<AddAddressRequest>
{
    public AddAddressRequestValidator()
    {
        RuleFor(x => x.Line1).NotEmpty().MaximumLength(300);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RegionCode).NotEmpty();
        RuleFor(x => x.Country).NotEmpty().MaximumLength(100);
    }
}

public sealed record UpdateAddressRequest(AddressKind Kind, string Line1, string? Line2, string City, string RegionCode, string Country, string? PostalCode, double? Latitude, double? Longitude);

public sealed class UpdateAddressRequestValidator : AbstractValidator<UpdateAddressRequest>
{
    public UpdateAddressRequestValidator()
    {
        RuleFor(x => x.Line1).NotEmpty().MaximumLength(300);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RegionCode).NotEmpty();
        RuleFor(x => x.Country).NotEmpty().MaximumLength(100);
    }
}

public sealed record AddContactRequest(string FullName, string Email, string? Phone, string? Role);

public sealed class AddContactRequestValidator : AbstractValidator<AddContactRequest>
{
    public AddContactRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
    }
}

public sealed record AddBranchRequest(string NameAr, string NameEn, Guid? AddressId);

public sealed class AddBranchRequestValidator : AbstractValidator<AddBranchRequest>
{
    public AddBranchRequestValidator()
    {
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
    }
}

public sealed record UpdateBranchRequest(string NameAr, string NameEn, Guid? AddressId, bool IsActive);

public sealed class UpdateBranchRequestValidator : AbstractValidator<UpdateBranchRequest>
{
    public UpdateBranchRequestValidator()
    {
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
    }
}

public sealed record AddBankAccountRequest(string AccountHolderName, string BankName, string? BranchName, string AccountNumber, string? SwiftBic, string CurrencyCode);

public sealed class AddBankAccountRequestValidator : AbstractValidator<AddBankAccountRequest>
{
    public AddBankAccountRequestValidator()
    {
        RuleFor(x => x.AccountHolderName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BankName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AccountNumber).NotEmpty().MaximumLength(64);
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3);
    }
}

public sealed record UpdateBankAccountRequest(string AccountHolderName, string BankName, string? BranchName, string? AccountNumber, string? SwiftBic, string CurrencyCode);

public sealed class UpdateBankAccountRequestValidator : AbstractValidator<UpdateBankAccountRequest>
{
    public UpdateBankAccountRequestValidator()
    {
        RuleFor(x => x.AccountHolderName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BankName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AccountNumber).MaximumLength(64).When(x => x.AccountNumber is not null);
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3);
    }
}

public sealed record LinkCategoryRequest(string CategoryCode);

public static class SupplierEndpoints
{
    public static void MapSupplierEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/suppliers").WithTags("Suppliers");

        group.MapGet("/{referenceCode}", async (
            string referenceCode,
            IGetSupplierHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(referenceCode, ct);

            return result switch
            {
                GetSupplierResult.Found f => Results.Ok(f.Supplier),
                GetSupplierResult.NotFoundOrOutOfScope => Results.NotFound(),
                _ => Results.Problem(),
            };
        })
        .RequireAuthorization()
        .WithETag()
        .WithName("GetSupplier");

        group.MapGet("/me", async (IGetSupplierHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleOwnAsync(ct);

            return result switch
            {
                GetSupplierResult.Found f => Results.Ok(f.Supplier),
                GetSupplierResult.NotFoundOrOutOfScope => Results.NotFound(),
                _ => Results.Problem(),
            };
        })
        .RequireAuthorization()
        .WithETag()
        .WithName("GetOwnSupplier");

        static IResult MapProfileResult(UpdateProfileResult result) => result switch
        {
            UpdateProfileResult.Success s => Results.Ok(s.Supplier),
            UpdateProfileResult.NotFoundOrOutOfScope => Results.NotFound(),
            UpdateProfileResult.Conflict c => new StaleVersionResult(c.CurrentRowVersion),
            UpdateProfileResult.NotEditable n => Results.Json(
                new { error = "field_not_flagged", detail = n.Reason },
                statusCode: StatusCodes.Status403Forbidden),
            UpdateProfileResult.InvalidState i => Results.Conflict(new { error = i.Reason }),
            _ => Results.Problem(),
        };

        static IResult MapMutation(ProfileMutationResult result) => result switch
        {
            ProfileMutationResult.Success s => Results.Ok(s.Supplier),
            ProfileMutationResult.NotFoundOrOutOfScope => Results.NotFound(),
            ProfileMutationResult.NotEditable n => Results.Json(
                new { error = "field_not_flagged", detail = n.Reason },
                statusCode: StatusCodes.Status403Forbidden),
            ProfileMutationResult.InvalidState i => Results.Conflict(new { error = i.Reason }),
            _ => Results.Problem(),
        };

        group.MapPatch("/{supplierCode}", async (
            string supplierCode,
            UpdateProfileRequest request,
            IValidator<UpdateProfileRequest> validator,
            ISupplierCodeScope codeScope,
            IUpdateProfileHandler handler,
            CancellationToken ct) =>
        {
            if (await codeScope.ResolveOwnAsync(supplierCode, ct) is null) return Results.NotFound();

            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.HandleAsync(new UpdateProfileCommand(request.Description, request.Website, request.SupplierGroup, request.CurrencyCode, request.PrimaryContactPhone), ct);

            return MapProfileResult(result);
        })
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("UpdateSupplierProfile");

        group.MapPut("/me/legal-info", async (
            UpdateLegalInfoRequest request,
            IValidator<UpdateLegalInfoRequest> validator,
            IUpdateLegalInfoHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.HandleAsync(new UpdateLegalInfoCommand(request.LegalNameAr, request.LegalNameEn, request.RegistrationNumber, request.TaxId, request.SupplierType, request.EstablishedOn), ct);

            return MapProfileResult(result);
        })
        .RequirePermission(Permissions.SupplierEdit)
        .WithFreshETag()
        .WithName("UpdateLegalInfo");

        group.MapPost("/me/logo", async (
            HttpRequest request,
            IUploadLogoHandler handler,
            CancellationToken ct) =>
        {
            if (!request.HasFormContentType) return Results.BadRequest(new { error = "expected_multipart_form" });

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0) return Results.BadRequest(new { error = "file_required" });

            await using var stream = file.OpenReadStream();
            var result = await handler.HandleAsync(new UploadLogoCommand(stream, file.FileName, file.Length), ct);

            return result switch
            {
                UploadLogoResult.Success s => Results.Ok(s.Supplier),
                UploadLogoResult.NotFoundOrOutOfScope => Results.NotFound(),
                UploadLogoResult.TooLarge => Results.BadRequest(new { error = "file_too_large" }),
                UploadLogoResult.UnsupportedType => Results.BadRequest(new { error = "unsupported_file_type" }),
                UploadLogoResult.ContentMismatch => Results.BadRequest(new { error = "content_type_mismatch" }),
                UploadLogoResult.NotEditable n => Results.Conflict(new { error = n.Reason }),
                _ => Results.Problem(),
            };
        })
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("UploadLogo")
        .DisableAntiforgery();

        group.MapGet("/me/logo/download-url", async (IGetLogoDownloadUrlHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(ct);
            return result switch
            {
                LogoDownloadUrlResult.Success s => Results.Ok(new { url = s.Url }),
                LogoDownloadUrlResult.NotFoundOrOutOfScope => Results.NotFound(),
                _ => Results.Problem(),
            };
        })
        .RequireAuthorization()
        .WithName("GetLogoDownloadUrl");

        group.MapPost("/me/representatives", async (
            AddRepresentativeRequest request,
            IValidator<AddRepresentativeRequest> validator,
            IManageRepresentativeHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.AddAsync(new AddRepresentativeCommand(request.FullName, request.Email, request.Phone, request.Position), ct);
            return MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("AddRepresentative");

        group.MapPut("/me/representatives/{representativeId:guid}", async (
            Guid representativeId,
            AddRepresentativeRequest request,
            IValidator<AddRepresentativeRequest> validator,
            IManageRepresentativeHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.UpdateAsync(new UpdateRepresentativeCommand(representativeId, request.FullName, request.Email, request.Phone, request.Position), ct);
            return MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("UpdateRepresentative");

        group.MapDelete("/me/representatives/{representativeId:guid}", async (Guid representativeId, IManageRepresentativeHandler handler, CancellationToken ct) =>
            MapMutation(await handler.RemoveAsync(new RemoveRepresentativeCommand(representativeId), ct)))
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("RemoveRepresentative");

        group.MapPost("/me/representatives/{representativeId:guid}/set-primary", async (Guid representativeId, IManageRepresentativeHandler handler, CancellationToken ct) =>
            MapMutation(await handler.SetPrimaryAsync(new SetPrimaryRepresentativeCommand(representativeId), ct)))
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("SetPrimaryRepresentative");

        group.MapPost("/me/addresses", async (
            AddAddressRequest request,
            IValidator<AddAddressRequest> validator,
            IManageAddressHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.AddAsync(new AddAddressCommand(request.Kind, request.Line1, request.Line2, request.City, request.RegionCode, request.Country, request.PostalCode, request.Latitude, request.Longitude), ct);
            return MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("AddAddress");

        group.MapPut("/me/addresses/{addressId:guid}", async (
            Guid addressId,
            UpdateAddressRequest request,
            IValidator<UpdateAddressRequest> validator,
            IManageAddressHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.UpdateAsync(new UpdateAddressCommand(addressId, request.Kind, request.Line1, request.Line2, request.City, request.RegionCode, request.Country, request.PostalCode, request.Latitude, request.Longitude), ct);
            return MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("UpdateAddress");

        group.MapDelete("/me/addresses/{addressId:guid}", async (Guid addressId, IManageAddressHandler handler, CancellationToken ct) =>
            MapMutation(await handler.RemoveAsync(new RemoveAddressCommand(addressId), ct)))
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("RemoveAddress");

        group.MapPost("/me/contacts", async (
            AddContactRequest request,
            IValidator<AddContactRequest> validator,
            IManageContactHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.AddAsync(new AddContactCommand(request.FullName, request.Email, request.Phone, request.Role), ct);
            return MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("AddContact");

        group.MapPut("/me/contacts/{contactId:guid}", async (
            Guid contactId,
            AddContactRequest request,
            IValidator<AddContactRequest> validator,
            IManageContactHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.UpdateAsync(new UpdateContactCommand(contactId, request.FullName, request.Email, request.Phone, request.Role), ct);
            return MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("UpdateContact");

        group.MapDelete("/me/contacts/{contactId:guid}", async (Guid contactId, IManageContactHandler handler, CancellationToken ct) =>
            MapMutation(await handler.RemoveAsync(new RemoveContactCommand(contactId), ct)))
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("RemoveContact");

        group.MapPost("/me/branches", async (
            AddBranchRequest request,
            IValidator<AddBranchRequest> validator,
            IManageBranchHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.AddAsync(new AddBranchCommand(request.NameAr, request.NameEn, request.AddressId), ct);
            return MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("AddBranch");

        group.MapPut("/me/branches/{branchId:guid}", async (
            Guid branchId,
            UpdateBranchRequest request,
            IValidator<UpdateBranchRequest> validator,
            IManageBranchHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.UpdateAsync(new UpdateBranchCommand(branchId, request.NameAr, request.NameEn, request.AddressId, request.IsActive), ct);
            return MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("UpdateBranch");

        group.MapDelete("/me/branches/{branchId:guid}", async (Guid branchId, IManageBranchHandler handler, CancellationToken ct) =>
            MapMutation(await handler.RemoveAsync(new RemoveBranchCommand(branchId), ct)))
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("RemoveBranch");

        group.MapPost("/me/bank-accounts", async (
            AddBankAccountRequest request,
            IValidator<AddBankAccountRequest> validator,
            IManageBankAccountHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.AddAsync(new AddBankAccountCommand(request.AccountHolderName, request.BankName, request.BranchName, request.AccountNumber, request.SwiftBic, request.CurrencyCode), ct);
            return MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierBankAccountManage)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("AddBankAccount");

        group.MapPut("/me/bank-accounts/{bankAccountId:guid}", async (
            Guid bankAccountId,
            UpdateBankAccountRequest request,
            IValidator<UpdateBankAccountRequest> validator,
            IManageBankAccountHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.UpdateAsync(new UpdateBankAccountCommand(bankAccountId, request.AccountHolderName, request.BankName, request.BranchName, request.AccountNumber, request.SwiftBic, request.CurrencyCode), ct);
            return MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierBankAccountManage)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("UpdateBankAccount");

        group.MapDelete("/me/bank-accounts/{bankAccountId:guid}", async (Guid bankAccountId, IManageBankAccountHandler handler, CancellationToken ct) =>
            MapMutation(await handler.RemoveAsync(new RemoveBankAccountCommand(bankAccountId), ct)))
        .RequirePermission(Permissions.SupplierBankAccountManage)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("RemoveBankAccount");

        group.MapPost("/me/bank-accounts/{bankAccountId:guid}/set-default", async (Guid bankAccountId, IManageBankAccountHandler handler, CancellationToken ct) =>
            MapMutation(await handler.SetDefaultAsync(new SetDefaultBankAccountCommand(bankAccountId), ct)))
        .RequirePermission(Permissions.SupplierBankAccountManage)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("SetDefaultBankAccount");

        group.MapPost("/me/bank-accounts/{bankAccountId:guid}/reveal", async (Guid bankAccountId, IManageBankAccountHandler handler, CancellationToken ct) =>
        {
            var result = await handler.RevealAsync(new RevealBankAccountCommand(bankAccountId), ct);
            return result switch
            {
                RevealBankAccountResult.Success s => Results.Ok(new { accountNumber = s.AccountNumber }),
                RevealBankAccountResult.NotFoundOrOutOfScope => Results.NotFound(),
                _ => Results.Problem(),
            };
        })
        .RequirePermission(Permissions.SupplierBankAccountManage)
        .WithName("RevealBankAccount");

        group.MapPost("/me/category-links", async (LinkCategoryRequest request, IManageCategoryLinkHandler handler, CancellationToken ct) =>
            MapMutation(await handler.LinkAsync(new LinkCategoryCommand(request.CategoryCode), ct)))
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("LinkCategory");

        group.MapDelete("/me/category-links/{categoryCode}", async (string categoryCode, IManageCategoryLinkHandler handler, CancellationToken ct) =>
            MapMutation(await handler.UnlinkAsync(new UnlinkCategoryCommand(categoryCode), ct)))
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("UnlinkCategory");

        group.MapPost("/me/accept-terms", async (
            IAcceptTermsHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(ct);

            return result switch
            {
                AcceptTermsResult.Success s => Results.Ok(s.Supplier),
                AcceptTermsResult.NotFoundOrOutOfScope => Results.NotFound(),
                AcceptTermsResult.InvalidState i => Results.Conflict(new { error = i.Reason }),
                _ => Results.Problem(),
            };
        })
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("AcceptTerms");

        group.MapPost("/{supplierCode}/onboarding/submit", async (
            string supplierCode,
            ISupplierCodeScope codeScope,
            ISubmitApplicationHandler handler,
            CancellationToken ct) =>
        {
            if (await codeScope.ResolveOwnAsync(supplierCode, ct) is null) return Results.NotFound();

            var result = await handler.HandleAsync(ct);

            return result switch
            {
                SubmitApplicationResult.Success s => Results.Ok(s.Supplier),
                SubmitApplicationResult.NotFoundOrOutOfScope => Results.NotFound(),
                SubmitApplicationResult.Incomplete i => Results.UnprocessableEntity(new { error = "incomplete_profile", missingFields = i.MissingFields }),
                SubmitApplicationResult.InvalidState s => Results.Conflict(new { error = s.Reason }),
                _ => Results.Problem(),
            };
        })
        .RequirePermission(Permissions.SupplierSubmit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("SubmitSupplierApplication");
    }
}

internal sealed record StaleVersionResult(uint CurrentRowVersion) : IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.SetETag(CurrentRowVersion);
        return ProblemResponse.WriteAsync(httpContext, ProblemResponse.Build(
            httpContext, StatusCodes.Status412PreconditionFailed, ProblemTypes.PreconditionFailed,
            "The precondition failed.", "ETAG_MISMATCH",
            "This resource changed after you loaded it. Refetch it and reapply your change."));
    }
}
