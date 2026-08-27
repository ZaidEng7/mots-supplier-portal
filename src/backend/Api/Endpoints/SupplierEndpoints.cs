using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Api.Endpoints;

public sealed record UpdateProfileRequest(string? Description, string? Website, string? SupplierGroup, string? CurrencyCode, string? PrimaryContactPhone);

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator(AppDbContext db)
    {
        RuleFor(x => x.CurrencyCode).Length(3).When(x => x.CurrencyCode is not null);
        RuleFor(x => x.CurrencyCode)
            .MustAsync(async (code, ct) => await db.Currencies.AnyAsync(c => c.Code == code && c.IsActive, ct))
            .WithMessage("Unknown or inactive currency code.")
            .When(x => x.CurrencyCode is not null);
    }
}

public sealed record UpdateLegalInfoRequest(string LegalNameAr, string LegalNameEn, string? RegistrationNumber, string? TaxId, SupplierLegalType SupplierType, DateOnly? EstablishedOn);

/// <summary>FEAT-04.2 [ASSUMPTION 2026-08-27]: requiredness is config-driven
/// (SupplierFieldConfig, category LegalInfoRequired) rather than hardcoded - MaximumLength stays
/// a fixed schema constraint (column width), only NotEmpty/required-ness is configurable.</summary>
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
                context.AddFailure(nameof(request.LegalNameAr), "'Legal Name Ar' must not be empty.");
            if (required.Contains("legalNameEn") && string.IsNullOrWhiteSpace(request.LegalNameEn))
                context.AddFailure(nameof(request.LegalNameEn), "'Legal Name En' must not be empty.");
            if (required.Contains("registrationNumber") && string.IsNullOrWhiteSpace(request.RegistrationNumber))
                context.AddFailure(nameof(request.RegistrationNumber), "'Registration Number' must not be empty.");
            if (required.Contains("taxId") && string.IsNullOrWhiteSpace(request.TaxId))
                context.AddFailure(nameof(request.TaxId), "'Tax Id' must not be empty.");
            if (required.Contains("establishedOn") && request.EstablishedOn is null)
                context.AddFailure(nameof(request.EstablishedOn), "'Established On' must not be empty.");
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

/// <summary>AccountNumber is optional here (see UpdateBankAccountCommand's doc comment) - null
/// leaves the existing encrypted value untouched.</summary>
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

        // Authenticated + row-scoped (STORY-01.8.1); no specific permission needed - any
        // authenticated supplier user may look up their own supplier record.
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
        .WithName("GetOwnSupplier");

        static IResult MapMutation(ProfileMutationResult result) => result switch
        {
            ProfileMutationResult.Success s => Results.Ok(s.Supplier),
            ProfileMutationResult.NotFoundOrOutOfScope => Results.NotFound(),
            ProfileMutationResult.InvalidState i => Results.Conflict(new { error = i.Reason }),
            _ => Results.Problem(),
        };

        // Self-service: the caller's own supplier record, resolved from the JWT's supplierId
        // claim (row-scoped) rather than a path parameter - the SPA never needs to know its
        // own reference code to drive onboarding.
        group.MapPatch("/me/profile", async (
            UpdateProfileRequest request,
            IValidator<UpdateProfileRequest> validator,
            IUpdateProfileHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var result = await handler.HandleAsync(new UpdateProfileCommand(request.Description, request.Website, request.SupplierGroup, request.CurrencyCode, request.PrimaryContactPhone), ct);

            return result switch
            {
                UpdateProfileResult.Success s => Results.Ok(s.Supplier),
                UpdateProfileResult.NotFoundOrOutOfScope => Results.NotFound(),
                UpdateProfileResult.InvalidState i => Results.Conflict(new { error = i.Reason }),
                _ => Results.Problem(),
            };
        })
        .RequirePermission(Permissions.SupplierEdit)
        .WithName("UpdateSupplierProfile");

        // FEAT-04.2/MSP-51.
        group.MapPut("/me/legal-info", async (
            UpdateLegalInfoRequest request,
            IValidator<UpdateLegalInfoRequest> validator,
            IUpdateLegalInfoHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var result = await handler.HandleAsync(new UpdateLegalInfoCommand(request.LegalNameAr, request.LegalNameEn, request.RegistrationNumber, request.TaxId, request.SupplierType, request.EstablishedOn), ct);

            return result switch
            {
                UpdateProfileResult.Success s => Results.Ok(s.Supplier),
                UpdateProfileResult.NotFoundOrOutOfScope => Results.NotFound(),
                UpdateProfileResult.InvalidState i => Results.Conflict(new { error = i.Reason }),
                _ => Results.Problem(),
            };
        })
        .RequirePermission(Permissions.SupplierEdit)
        .WithName("UpdateLegalInfo");

        // FEAT-04.1: previously a dead field (SetLogo existed, nothing called it).
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

        // FEAT-04.4/MSP-52: add/edit/remove representatives with primary designation.
        group.MapPost("/me/representatives", async (
            AddRepresentativeRequest request,
            IValidator<AddRepresentativeRequest> validator,
            IManageRepresentativeHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var result = await handler.AddAsync(new AddRepresentativeCommand(request.FullName, request.Email, request.Phone, request.Position), ct);
            return MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierEdit)
        .WithName("AddRepresentative");

        group.MapPut("/me/representatives/{representativeId:guid}", async (
            Guid representativeId,
            AddRepresentativeRequest request,
            IValidator<AddRepresentativeRequest> validator,
            IManageRepresentativeHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var result = await handler.UpdateAsync(new UpdateRepresentativeCommand(representativeId, request.FullName, request.Email, request.Phone, request.Position), ct);
            return MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierEdit)
        .WithName("UpdateRepresentative");

        group.MapDelete("/me/representatives/{representativeId:guid}", async (Guid representativeId, IManageRepresentativeHandler handler, CancellationToken ct) =>
            MapMutation(await handler.RemoveAsync(new RemoveRepresentativeCommand(representativeId), ct)))
        .RequirePermission(Permissions.SupplierEdit)
        .WithName("RemoveRepresentative");

        group.MapPost("/me/representatives/{representativeId:guid}/set-primary", async (Guid representativeId, IManageRepresentativeHandler handler, CancellationToken ct) =>
            MapMutation(await handler.SetPrimaryAsync(new SetPrimaryRepresentativeCommand(representativeId), ct)))
        .RequirePermission(Permissions.SupplierEdit)
        .WithName("SetPrimaryRepresentative");

        // FEAT-04.3/MSP-52.
        group.MapPost("/me/addresses", async (
            AddAddressRequest request,
            IValidator<AddAddressRequest> validator,
            IManageAddressHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var result = await handler.AddAsync(new AddAddressCommand(request.Kind, request.Line1, request.Line2, request.City, request.RegionCode, request.Country, request.PostalCode, request.Latitude, request.Longitude), ct);
            return MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierEdit)
        .WithName("AddAddress");

        group.MapPut("/me/addresses/{addressId:guid}", async (
            Guid addressId,
            UpdateAddressRequest request,
            IValidator<UpdateAddressRequest> validator,
            IManageAddressHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var result = await handler.UpdateAsync(new UpdateAddressCommand(addressId, request.Kind, request.Line1, request.Line2, request.City, request.RegionCode, request.Country, request.PostalCode, request.Latitude, request.Longitude), ct);
            return MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierEdit)
        .WithName("UpdateAddress");

        group.MapDelete("/me/addresses/{addressId:guid}", async (Guid addressId, IManageAddressHandler handler, CancellationToken ct) =>
            MapMutation(await handler.RemoveAsync(new RemoveAddressCommand(addressId), ct)))
        .RequirePermission(Permissions.SupplierEdit)
        .WithName("RemoveAddress");

        // FEAT-04.4/MSP-52.
        group.MapPost("/me/contacts", async (
            AddContactRequest request,
            IValidator<AddContactRequest> validator,
            IManageContactHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var result = await handler.AddAsync(new AddContactCommand(request.FullName, request.Email, request.Phone, request.Role), ct);
            return MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierEdit)
        .WithName("AddContact");

        group.MapPut("/me/contacts/{contactId:guid}", async (
            Guid contactId,
            AddContactRequest request,
            IValidator<AddContactRequest> validator,
            IManageContactHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var result = await handler.UpdateAsync(new UpdateContactCommand(contactId, request.FullName, request.Email, request.Phone, request.Role), ct);
            return MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierEdit)
        .WithName("UpdateContact");

        group.MapDelete("/me/contacts/{contactId:guid}", async (Guid contactId, IManageContactHandler handler, CancellationToken ct) =>
            MapMutation(await handler.RemoveAsync(new RemoveContactCommand(contactId), ct)))
        .RequirePermission(Permissions.SupplierEdit)
        .WithName("RemoveContact");

        // FEAT-04.5/MSP-53.
        group.MapPost("/me/branches", async (
            AddBranchRequest request,
            IValidator<AddBranchRequest> validator,
            IManageBranchHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var result = await handler.AddAsync(new AddBranchCommand(request.NameAr, request.NameEn, request.AddressId), ct);
            return MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierEdit)
        .WithName("AddBranch");

        group.MapPut("/me/branches/{branchId:guid}", async (
            Guid branchId,
            UpdateBranchRequest request,
            IValidator<UpdateBranchRequest> validator,
            IManageBranchHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var result = await handler.UpdateAsync(new UpdateBranchCommand(branchId, request.NameAr, request.NameEn, request.AddressId, request.IsActive), ct);
            return MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierEdit)
        .WithName("UpdateBranch");

        group.MapDelete("/me/branches/{branchId:guid}", async (Guid branchId, IManageBranchHandler handler, CancellationToken ct) =>
            MapMutation(await handler.RemoveAsync(new RemoveBranchCommand(branchId), ct)))
        .RequirePermission(Permissions.SupplierEdit)
        .WithName("RemoveBranch");

        // FEAT-04.6/MSP-53: supplier.bankAccount.manage (supplier_admin only), never supplier.edit.
        group.MapPost("/me/bank-accounts", async (
            AddBankAccountRequest request,
            IValidator<AddBankAccountRequest> validator,
            IManageBankAccountHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var result = await handler.AddAsync(new AddBankAccountCommand(request.AccountHolderName, request.BankName, request.BranchName, request.AccountNumber, request.SwiftBic, request.CurrencyCode), ct);
            return MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierBankAccountManage)
        .WithName("AddBankAccount");

        group.MapPut("/me/bank-accounts/{bankAccountId:guid}", async (
            Guid bankAccountId,
            UpdateBankAccountRequest request,
            IValidator<UpdateBankAccountRequest> validator,
            IManageBankAccountHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var result = await handler.UpdateAsync(new UpdateBankAccountCommand(bankAccountId, request.AccountHolderName, request.BankName, request.BranchName, request.AccountNumber, request.SwiftBic, request.CurrencyCode), ct);
            return MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierBankAccountManage)
        .WithName("UpdateBankAccount");

        group.MapDelete("/me/bank-accounts/{bankAccountId:guid}", async (Guid bankAccountId, IManageBankAccountHandler handler, CancellationToken ct) =>
            MapMutation(await handler.RemoveAsync(new RemoveBankAccountCommand(bankAccountId), ct)))
        .RequirePermission(Permissions.SupplierBankAccountManage)
        .WithName("RemoveBankAccount");

        group.MapPost("/me/bank-accounts/{bankAccountId:guid}/set-default", async (Guid bankAccountId, IManageBankAccountHandler handler, CancellationToken ct) =>
            MapMutation(await handler.SetDefaultAsync(new SetDefaultBankAccountCommand(bankAccountId), ct)))
        .RequirePermission(Permissions.SupplierBankAccountManage)
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

        // FEAT-04.7/MSP-54.
        group.MapPost("/me/category-links", async (LinkCategoryRequest request, IManageCategoryLinkHandler handler, CancellationToken ct) =>
            MapMutation(await handler.LinkAsync(new LinkCategoryCommand(request.CategoryCode), ct)))
        .RequirePermission(Permissions.SupplierEdit)
        .WithName("LinkCategory");

        group.MapDelete("/me/category-links/{categoryCode}", async (string categoryCode, IManageCategoryLinkHandler handler, CancellationToken ct) =>
            MapMutation(await handler.UnlinkAsync(new UnlinkCategoryCommand(categoryCode), ct)))
        .RequirePermission(Permissions.SupplierEdit)
        .WithName("UnlinkCategory");

        // BRULE-009: explicit T&C acceptance, recorded with version + timestamp, gating
        // submit alongside profile completeness and required documents (BRULE-004).
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
        .WithName("AcceptTerms");

        group.MapPost("/me/submit-application", async (
            ISubmitApplicationHandler handler,
            CancellationToken ct) =>
        {
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
        .WithName("SubmitSupplierApplication");
    }
}
