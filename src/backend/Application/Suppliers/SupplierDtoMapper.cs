using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Application.Suppliers;

public static class SupplierDtoMapper
{
    public static SupplierDto ToDto(Supplier supplier)
    {
        var primaryPhone = supplier.Representatives.FirstOrDefault(r => r.IsPrimary)?.Phone;

        return new SupplierDto(
            supplier.ReferenceCode,
            supplier.DisplayNameAr,
            supplier.DisplayNameEn,
            supplier.Description,
            supplier.Website,
            supplier.LogoStorageKey,
            supplier.SupplierGroup,
            supplier.OnboardingState.ToString(),
            supplier.LifecycleState.ToString(),
            supplier.CurrencyCode,
            supplier.LegalInfo is null
                ? null
                : new LegalInfoDto(
                    supplier.LegalInfo.LegalNameAr,
                    supplier.LegalInfo.LegalNameEn,
                    supplier.LegalInfo.RegistrationNumber,
                    supplier.LegalInfo.TaxId,
                    supplier.LegalInfo.SupplierType.ToString(),
                    supplier.LegalInfo.EstablishedOn),
            primaryPhone,
            [.. supplier.Representatives.Select(r => new RepresentativeDto(r.Id, r.FullName, r.Email, r.Phone, r.Position, r.IsPrimary))],
            [.. supplier.Addresses.Select(a => new AddressDto(a.Id, a.Kind.ToString(), a.Line1, a.Line2, a.City, a.RegionCode, a.Country, a.PostalCode, a.Latitude, a.Longitude, a.IsPrimary))],
            [.. supplier.Contacts.Select(c => new ContactDto(c.Id, c.FullName, c.Email, c.Phone, c.Role))],
            [.. supplier.Branches.Select(b => new BranchDto(b.Id, b.NameAr, b.NameEn, b.AddressId, b.IsActive))],
            [.. supplier.BankAccounts.Select(b => new BankAccountDto(b.Id, b.AccountHolderName, b.BankName, b.BranchName, b.MaskedAccountNumber, b.SwiftBic, b.CurrencyCode, b.IsDefault))],
            [.. supplier.CategoryLinks.Select(l => l.CategoryCode)],
            supplier.GetMissingProfileFields(),
            supplier.TermsAcceptedVersion,
            supplier.TermsAcceptedAt,
            supplier.RowVersion);
    }
}
