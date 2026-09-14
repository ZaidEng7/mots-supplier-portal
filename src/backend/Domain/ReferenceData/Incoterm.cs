// A delivery term, such as DDP or FOB: who pays for transport and where risk passes from
// the seller to the buyer. A supplier picks one when setting the commercial terms of a bid.
//
// Before this list existed, that field was free text validated only by its length. A
// supplier could submit "ASAP", a space, or a misspelt "FOP", and the comparison matrix
// printed it beside the real terms. Two bids using different words for the same term
// compared as different; two using the same word for different terms compared as the same.
//
// The seeded set is Incoterms 2020, all eleven, unabridged. The rule is an international
// standard rather than a ministry list, so shipping a subset would be inventing
// procurement policy. Which of the eleven a buying body actually accepts IS a ministry
// decision, and it is taken by deactivating the ones it does not want: that hides a term
// from new bids and leaves every historical bid readable. This is why the table needs no
// policy column of its own.
//
// Codes are three letters because the standard's are.

namespace MotsSupplierPortal.Domain.ReferenceData;

public sealed class Incoterm
{
    public Guid Id { get; init; }
    public required string Code { get; init; }
    public required string NameAr { get; set; }
    public required string NameEn { get; set; }
    public bool IsActive { get; set; } = true;
}
