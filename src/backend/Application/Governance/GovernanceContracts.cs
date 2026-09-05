namespace MotsSupplierPortal.Application.Governance;

/// <summary>One aggregate count, keyed by the state or category it counts.</summary>
public sealed record GovernanceCountDto(string Key, int Count);

/// <summary>
/// FR-DSH-005/SCR-600: the Ministry's cross-organization governance overview.
///
/// <para><b>Every figure here is an aggregate, and that is the contract, not a coincidence.</b>
/// BRULE-086 grants the Ministry "read-only, cross-organization access to aggregate/governance
/// metrics only" - no named supplier, no named RFQ, no reviewer's free text. Nothing on this DTO
/// identifies a row, so there is no filter to forget.</para>
///
/// <para><b><see cref="TotalAwardedValue"/> is null unless the commercial-visibility flag is on</b>
/// (D-6/BRULE-087, seeded off). It is the ONLY commercial figure in the whole shape, deliberately:
/// one nullable field is a policy answer flipping a value, whereas a commercial variant of every
/// number would be a second DTO nobody could keep in step.</para>
/// </summary>
public sealed record GovernanceOverviewDto(
    int TotalSuppliers,
    IReadOnlyList<GovernanceCountDto> SuppliersByLifecycleState,
    int TotalRfqs,
    IReadOnlyList<GovernanceCountDto> RfqsByState,
    int TotalAwards,
    /// <summary>Proposals received per published RFQ, to one decimal. Participation is the metric
    /// BRULE-086's "governance" is about - whether the market is actually competing - and it is an
    /// average, so it names nobody.</summary>
    decimal AverageProposalsPerRfq,
    /// <summary>Null when the commercial-visibility flag is off, which is its seeded state. Null is
    /// not zero: "policy withholds this" and "the ministry has awarded nothing" are different facts
    /// and a reader must be able to tell them apart.</summary>
    decimal? TotalAwardedValue,
    /// <summary>Echoed so a screen can say WHY a figure is absent rather than rendering a blank.</summary>
    bool CommercialValuesVisible);

public interface IGetGovernanceOverviewHandler
{
    Task<GovernanceOverviewDto> HandleAsync(CancellationToken ct);
}
