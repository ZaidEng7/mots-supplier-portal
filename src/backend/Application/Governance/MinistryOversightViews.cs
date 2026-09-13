// The shapes behind the ministry's four detailed oversight screens.
//
//
// READ THIS BEFORE ADDING A FIELD
//
// Every other governance read in this codebase is an aggregate, by the default that says where visibility was
// undecided the ministry gets counts.
//
// These four are the exception, and the exception was granted by a person rather than derived from a rule. A
// relayed decision said the ministry may see commercial figures. A later recorded decision notes that these
// were built before the written sign-off that decision required, at the product owner's direction, to the
// widest of the four scopes offered: live tenders included, and per-bidder values shown.
//
//
// WHAT THAT MEANS CONCRETELY, SO NOBODY HAS TO INFER IT
//
// A ministry viewer can read what each named supplier has bid on a tender that is still open.
//
// That is the information a bidder's competitors would most like to have, and the role is held by people
// outside the buying body. It is not a defect and not an accident; it is a disclosure decision, and the
// record of it carries the argument against it as well as the instruction for it.
//
//
// THE SETTING STILL GOVERNS THE NUMBERS
//
// Every commercial value here is optional and is filled in only when the commercial-visibility setting is
// on.
//
// Absent is not zero. "Policy withholds this" and "nothing was bid" are different facts, and the screens say
// which one they are showing.
//
// Switching the setting off stops new disclosure. It does not un-show what has already been read.

namespace MotsSupplierPortal.Application.Governance;

using MotsSupplierPortal.Application.Common;

public sealed record MinistryRfqRowDto(
    string ReferenceCode,
    string TitleAr,
    string TitleEn,
    string State,
    string OrganizationNameAr,
    string OrganizationNameEn,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? SubmissionClosesAt,
    int InvitedSuppliers,
    int SubmittedProposals,
    decimal? AwardedValue,
    string? CurrencyCode);

public sealed record MinistrySupplierRowDto(
    string SupplierCode,
    string DisplayNameAr,
    string DisplayNameEn,
    string OnboardingState,
    string LifecycleState,
    IReadOnlyList<string> CategoryCodes,
    DateTimeOffset RegisteredAt,
    int SubmittedProposals,
    int AwardsWon,
    decimal? AwardedValue);

public sealed record MinistryAwardAnalyticsDto(
    int TotalAwards,
    decimal? TotalAwardedValue,
    IReadOnlyList<MinistrySpendBucketDto> ByMonth,
    IReadOnlyList<MinistrySpendBucketDto> ByCategory,
    IReadOnlyList<MinistrySpendBucketDto> ByOrganization,
    bool CommercialValuesVisible);

public sealed record MinistrySpendBucketDto(string Key, int Awards, decimal? Value, string? NameAr = null, string? NameEn = null);

public sealed record MinistryRfqDetailDto(
    MinistryRfqRowDto Summary,
    string? DescriptionAr,
    string? DescriptionEn,
    IReadOnlyList<MinistryRfqItemDto> Items,
    IReadOnlyList<MinistryBidDto> Bids,
    bool CommercialValuesVisible);

public sealed record MinistryRfqItemDto(
    string TitleAr, string TitleEn, string CategoryCode, decimal Quantity, string UnitOfMeasureCode);

public sealed record MinistryBidDto(
    string ProposalCode,
    string SupplierCode,
    string SupplierDisplayNameAr,
    string SupplierDisplayNameEn,
    string State,
    DateTimeOffset? SubmittedAt,
    decimal? TotalValue,
    bool IsAwarded);
