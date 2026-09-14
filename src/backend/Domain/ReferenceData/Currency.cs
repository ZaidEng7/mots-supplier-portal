// A currency a tender or a bid can be priced in, such as SYP or USD.
//
// Codes are three letters, following the ISO convention. The portal does not convert
// between currencies anywhere: an amount is shown in the currency it was entered in, and
// a tender's currency is fixed when it is created.
//
// Deactivating a currency hides it from new choices and leaves every historical record
// readable.

namespace MotsSupplierPortal.Domain.ReferenceData;

public sealed class Currency
{
    public Guid Id { get; init; }
    public required string Code { get; init; }
    public required string NameAr { get; set; }
    public required string NameEn { get; set; }
    public bool IsActive { get; set; } = true;
}
