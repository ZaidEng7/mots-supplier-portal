// What a caller may send to a supplier-profile route, and what counts as valid.
//
// One record per request shape, each with the checks that apply to it.
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
// CONFIGURABLE REQUIREDNESS
//
// Which legal-information fields are required is configuration rather than code, so a ministry can change
// it without a deployment. Length limits stay fixed, because those are column widths; only requiredness
// is configurable.
//
// Those configurable failures are raised with the validation library's own standard code rather than a
// bare message, so the message catalogue resolves them to Arabic exactly as a declared rule would.
// Without the code they would fall through to English.
//
//
// THE FOUNDING DATE
//
// A founding date in the future is refused, because no company has one. The screen offered one, because
// the date picker's calendar ran into next month, and this validation accepted it, so the registry could
// show a supplier founded after today.
//
// It is refused here as well as in the form, because the form is one of several ways in and the
// finance-system import is another.
//
//
// THE BANK ACCOUNT NUMBER
//
// It is optional on an update, where sending nothing leaves the stored encrypted value untouched.

namespace MotsSupplierPortal.Api.Endpoints;

using FluentValidation;
using FluentValidation.Results;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90).When(x => x.Latitude.HasValue);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180).When(x => x.Longitude.HasValue);
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
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90).When(x => x.Latitude.HasValue);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180).When(x => x.Longitude.HasValue);
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
