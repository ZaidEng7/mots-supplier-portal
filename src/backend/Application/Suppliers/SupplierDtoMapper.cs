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
            supplier.OnboardingState.ToString(),
            supplier.RegistrationNumber,
            supplier.TaxId,
            supplier.AddressLine,
            supplier.City,
            supplier.Country,
            supplier.CurrencyCode,
            primaryPhone,
            supplier.GetMissingProfileFields(),
            supplier.TermsAcceptedVersion,
            supplier.TermsAcceptedAt);
    }
}
