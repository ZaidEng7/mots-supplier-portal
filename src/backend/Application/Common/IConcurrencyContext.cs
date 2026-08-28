namespace MotsSupplierPortal.Application.Common;

/// <summary>
/// MSP-65: carries the row version the caller believes it is editing, so a write can be rejected
/// when someone else has changed the row in between (BRULE-098, FR-PROF-010, NFR-AVL-007).
///
/// Transported as the standard HTTP <c>If-Match</c> header rather than a field on every request
/// body, so the 25 mutating endpoints do not each need a new DTO field, and so the semantic is the
/// one HTTP already defines for exactly this. <see langword="null"/> means the caller did not
/// supply a version - see the handler-side policy note in SupplierConcurrency.
/// </summary>
public interface IConcurrencyContext
{
    uint? ExpectedRowVersion { get; }
}
