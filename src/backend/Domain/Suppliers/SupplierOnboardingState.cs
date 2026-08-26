namespace MotsSupplierPortal.Domain.Suppliers;

/// <summary>
/// Onboarding lifecycle (docs/architecture/00-foundational-decisions.md §5, canonical).
/// Draft -> EmailVerified -> ProfileInProgress -> Submitted -> UnderReview ->
/// (InfoRequested -> Resubmitted -> UnderReview)* -> Approved | Rejected.
/// </summary>
public enum SupplierOnboardingState
{
    Draft,
    EmailVerified,
    ProfileInProgress,
    Submitted,
    UnderReview,
    InfoRequested,
    Resubmitted,
    Approved,
    Rejected,
}
