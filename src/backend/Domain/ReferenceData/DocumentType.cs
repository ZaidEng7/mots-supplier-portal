namespace MotsSupplierPortal.Domain.ReferenceData;

/// <summary>
/// Configurable required-document catalog (FR-DOC-001, EPIC-21 reference data). Generic types only -
/// no invented Syrian-specific document rules (docs/product/ASSUMPTIONS.md ASM-020 pattern).
/// </summary>
public sealed class DocumentType
{
    public Guid Id { get; init; }
    public required string Code { get; init; }
    public required string NameAr { get; init; }
    public required string NameEn { get; init; }
    public bool IsRequired { get; init; }
    public bool ExpiryTracked { get; init; }

    /// <summary>
    /// BRULE-023: expiry of a document of this type auto-suspends the supplier.
    ///
    /// <para><b>Defaults to false on every seeded type, deliberately.</b> BUSINESS-RULES.md marks
    /// which types are award-critical as `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]`, and the
    /// two ways of being wrong here are not symmetric. Flagging a type the Ministry would not have
    /// chosen suspends real suppliers - it blocks their participation, and reactivating them later
    /// does not undo having been blocked. Flagging none leaves behaviour exactly as it is today.
    /// So the mechanism ships complete and dormant, and the Ministry's answer becomes a data
    /// change rather than a deployment.</para>
    ///
    /// <para>The consequence worth stating plainly: BRULE-023 does nothing in production until
    /// somebody sets this. That is the intended state, not an oversight - see
    /// docs/product/BLOCKED-DECISIONS.md.</para>
    /// </summary>
    public bool IsAwardCritical { get; init; }
    public bool IsActive { get; init; } = true;
}
