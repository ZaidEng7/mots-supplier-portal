using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Application.Governance;

/// <summary>
/// SCR-601/602/603/606: the four Ministry screens that were refused for two batches and are built under
/// D-66.
///
/// <para><b>Read this before adding a field.</b> Every other governance read in this codebase is an
/// aggregate, by BRULE-087's default: where visibility was undecided, the Ministry got counts. These four
/// are the exception, and they are an exception granted by a person rather than derived from a rule -
/// D-57 relayed that the Ministry may see commercial figures, and D-66 records that they were built
/// <b>before the written sign-off D-57 required</b>, at the product owner's direction, to the widest of the
/// four scopes offered: <b>live tenders included, and per-bidder values shown</b>.</para>
///
/// <para><b>What that means concretely, so nobody has to infer it.</b> A `ministry_viewer` can read what
/// each named supplier has bid on a tender that is still open. That is the information a bidder's
/// competitors would most like to have, and the role is held by people outside the buying body. It is not a
/// defect and it is not an accident; it is a disclosure decision, and D-66 carries the argument against it
/// as well as the instruction for it.</para>
///
/// <para><b>The flag still governs the numbers.</b> Every commercial value on these DTOs is nullable and is
/// populated only when <c>GovernanceVisibility.commercialValues</c> is on. Null is not zero: "policy
/// withholds this" and "nothing was bid" are different facts, and the screens say which they are showing.
/// Switching the flag off stops new disclosure; it does not un-show what has been read.</para>
/// </summary>
/// <param name="OrganizationNameEn">The buying body. Named rather than counted, which is itself part of what
/// these screens widen: the aggregate reads never carried it.</param>
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
    /// <summary>The awarded value, when this tender has been awarded AND the flag is on. Null while the
    /// tender is live, because there is no award yet - not because policy is withholding it.</summary>
    decimal? AwardedValue,
    string? CurrencyCode);

/// <summary>SCR-601: one supplier as the Ministry's registry view shows it. Identity and standing, no
/// documents - a reviewer's compliance view is a different screen for a different persona.</summary>
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
    /// <summary>Total value this supplier has been awarded, when the flag is on.</summary>
    decimal? AwardedValue);

/// <summary>SCR-603: award and spend analytics. Aggregates by month, category and buying body, plus the
/// award list the drill-down starts from.</summary>
public sealed record MinistryAwardAnalyticsDto(
    int TotalAwards,
    decimal? TotalAwardedValue,
    IReadOnlyList<MinistrySpendBucketDto> ByMonth,
    IReadOnlyList<MinistrySpendBucketDto> ByCategory,
    IReadOnlyList<MinistrySpendBucketDto> ByOrganization,
    bool CommercialValuesVisible);

/// <param name="Value">Null when the flag is off. The COUNT is always present, because a count of awards is
/// an aggregate BRULE-086 grants outright and was never the thing in question.</param>
/// <param name="NameAr">
/// The bucket's name in Arabic, where the key is a CODE rather than a word. Null where the key already
/// reads as one.
///
/// <para>The by-category buckets group on <c>CategoryCode</c>, so a Ministry reader met
/// <c>tour_operations</c> on a chart axis and in a table column - a domain identifier, in one language,
/// with an underscore in it. The names exist: the <c>reference.category</c> rows carry both, and the
/// category-coverage endpoint already returns them. This carries them here too rather than asking the
/// SPA to fetch a second endpoint to render the first one's labels.</para>
///
/// <para>Null for months, whose key is a date, and for buying bodies, whose key is already the
/// organisation's own name. A caller renders the name when there is one and the key when there is not.</para>
/// </param>
public sealed record MinistrySpendBucketDto(string Key, int Awards, decimal? Value, string? NameAr = null, string? NameEn = null);

/// <summary>
/// SCR-606: one tender, read-only, with every bid on it.
///
/// <para><b>This is the screen the disclosure decision is really about.</b> The bids carry the supplier's
/// name and its total, on a tender that may still be open - the widest of the four scopes D-57 offered. The
/// per-bidder list is empty until the submission window closes for nobody; it is populated the moment a bid
/// is submitted.</para>
/// </summary>
public sealed record MinistryRfqDetailDto(
    MinistryRfqRowDto Summary,
    string? DescriptionAr,
    string? DescriptionEn,
    IReadOnlyList<MinistryRfqItemDto> Items,
    IReadOnlyList<MinistryBidDto> Bids,
    bool CommercialValuesVisible);

public sealed record MinistryRfqItemDto(
    string TitleAr, string TitleEn, string CategoryCode, decimal Quantity, string UnitOfMeasureCode);

/// <param name="SupplierDisplayNameEn">The bidder, named. Under a narrower scope this would have been a
/// pseudonym - A-8 anonymises bidders for the evaluators themselves while scoring is open.</param>
/// <param name="TotalValue">What they bid, when the flag is on.</param>
public sealed record MinistryBidDto(
    string ProposalCode,
    string SupplierCode,
    string SupplierDisplayNameAr,
    string SupplierDisplayNameEn,
    string State,
    DateTimeOffset? SubmittedAt,
    decimal? TotalValue,
    bool IsAwarded);

public interface IListMinistryRfqsHandler
{
    Task<ListEnvelope<MinistryRfqRowDto>> HandleAsync(string? cursor, int? limit, bool withCount, string? state, string? q, CancellationToken ct);
}

public interface IListMinistrySuppliersHandler
{
    Task<ListEnvelope<MinistrySupplierRowDto>> HandleAsync(string? cursor, int? limit, bool withCount, string? lifecycleState, string? q, CancellationToken ct);
}

public interface IGetMinistryAwardAnalyticsHandler
{
    Task<MinistryAwardAnalyticsDto> HandleAsync(CancellationToken ct);
}

public interface IGetMinistryRfqDetailHandler
{
    Task<MinistryRfqDetailDto?> HandleAsync(string referenceCode, CancellationToken ct);
}
