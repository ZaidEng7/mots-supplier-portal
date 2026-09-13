// How far a supplier has got through registration and review.
//
//   Draft               the account exists and nothing else
//   EmailVerified       the email address has been confirmed
//   ProfileInProgress   the company is filling in its profile
//   Submitted           handed in for review
//   UnderReview         a reviewer has it
//   InfoRequested       the reviewer asked for something
//   Resubmitted         the company answered, and it goes back under review
//   Approved            admitted
//   Rejected            refused
//
// The information-request loop may repeat.

namespace MotsSupplierPortal.Domain.Suppliers;

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
