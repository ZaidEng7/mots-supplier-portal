namespace MotsSupplierPortal.Domain.ReferenceData;

/// <summary>FEAT-06.1 [ASSUMPTION]: same interim-list rationale as Category.cs - a minimal flat
/// list, built now because Offering.UnitOfMeasureCode is a hard requirement of FEAT-06.1 itself,
/// not scope creep from a real buyer-side unit catalog (EPIC-21) that may supersede it later.</summary>
public sealed class UnitOfMeasure
{
    public Guid Id { get; init; }
    public required string Code { get; init; }
    public required string NameAr { get; init; }
    public required string NameEn { get; init; }
    public bool IsActive { get; init; } = true;
}
