// Settings an administrator can change without a deployment, one row per field.
//
// Each row is a pair: which group of settings it belongs to, and which field inside that group.
// IsEnabled is the answer. Three groups exist.
//
// ComplianceRetrigger decides which supplier fields send an approved supplier back for review
// when they are edited. The field codes match what the re-trigger check already records:
// legalInfo, bankAccount, categoryLink.
//
// LegalInfoRequired decides which legal-information fields a supplier must fill in. The field
// codes match the property names on the legal information itself: legalNameAr, legalNameEn,
// registrationNumber, taxId, supplierType, establishedOn.
//
// GovernanceVisibility is the ministry's policy on showing commercial values, as one flag whose
// field code is commercialValues and whose default is off. Putting it here rather than inventing
// a new settings mechanism is deliberate: this table is already the answer to "a thing
// procurement decides that code must not", so the ministry's lawyers flip a row instead of
// commissioning a feature.
//
// All three replace lists that used to be hard-coded at the places that read them, so what was
// documented as configurable is now genuinely configurable.
//
// RowVersion is here because these rows are the ones worth refusing a race on. They decide
// whether editing a bank account re-opens a compliance review, so two administrators tightening
// and loosening the same control at once should be refused rather than quietly resolved in favour
// of whoever saved second.

namespace MotsSupplierPortal.Domain.Configuration;

using MotsSupplierPortal.Domain.Common;

public static class FieldConfigCategory
{
    public const string ComplianceRetrigger = "ComplianceRetrigger";

    public const string LegalInfoRequired = "LegalInfoRequired";

    public const string GovernanceVisibility = "GovernanceVisibility";
}

public sealed class SupplierFieldConfig : IVersionedAggregate
{
    public Guid Id { get; init; }
    public required string Category { get; init; }
    public required string FieldCode { get; init; }
    public bool IsEnabled { get; set; }

    public uint RowVersion { get; private set; }
}
