namespace MotsSupplierPortal.Domain.ReferenceData;

/// <summary>
/// The sixth reference table FR-ADM-004 names, and the one that did not exist.
///
/// <para><b>What its absence meant.</b> <c>Proposal.IncotermCode</c> was a free
/// <c>varchar(10)</c> validated by nothing but its length, so a supplier could submit "ASAP", an
/// empty-looking space, or a misspelt "FOP" as a delivery term, and the comparison matrix printed it
/// beside the real ones. Two bids using different words for the same term compared as different, and
/// two using the same word for different terms compared as the same.</para>
///
/// <para><b>The seeded set is Incoterms 2020 - all eleven, unabridged.</b> The rule is an ICC
/// standard rather than a ministry list, so inventing a subset here would be inventing procurement
/// policy. Which of the eleven a buying body actually permits IS a ministry decision, and the
/// existing admin surface is where it is taken: a term nobody should quote is deactivated, which
/// hides it from new proposals and leaves every historical one readable. That is D-28's rule, and it
/// is why this table needs no policy column of its own.</para>
///
/// <para>Codes are three letters because the standard's are. The other reference tables allow fifty,
/// and <c>Currency</c> allows three for the same reason this does.</para>
/// </summary>
public sealed class Incoterm
{
    public Guid Id { get; init; }
    public required string Code { get; init; }
    public required string NameAr { get; set; }
    public required string NameEn { get; set; }
    public bool IsActive { get; set; } = true;
}
