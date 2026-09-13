// The vocabulary for the ministry's governance overview, which reads across every buying organization.
//
//
// EVERY FIGURE IS AN AGGREGATE, AND THAT IS THE CONTRACT
//
// The ministry's grant is read-only, cross-organization access to aggregate figures only. No named supplier,
// no named tender, no reviewer's free text.
//
// Nothing in this shape identifies a row, so there is no per-row filter for a later edit to forget.
//
//
// THE ONE COMMERCIAL FIGURE
//
// The total awarded value is absent unless the commercial-visibility setting is switched on, and it ships
// switched off.
//
// It is the only commercial figure in the whole shape, deliberately. One optional field means the policy is
// answered by flipping a value, whereas a commercial variant of every number would be a second shape nobody
// could keep in step.
//
//
// TWO DEFINITIONS WORTH STATING
//
// The award count is awards that have actually been issued, not award records. A recommendation that has not
// been approved is a record from the moment it is made, and calling that an award on the ministry's headline
// figure disagreed with both the value beside it and the screen it drills into, which have always counted
// issued awards only.
//
// The participation figure is bids received per published tender, to one decimal. Whether the market is
// actually competing is what governance is about.

namespace MotsSupplierPortal.Application.Governance;

public sealed record GovernanceCountDto(string Key, int Count);

public sealed record GovernanceOverviewDto(
    int TotalSuppliers,
    IReadOnlyList<GovernanceCountDto> SuppliersByLifecycleState,
    int TotalRfqs,
    IReadOnlyList<GovernanceCountDto> RfqsByState,
    int TotalAwards,
    decimal AverageProposalsPerRfq,
    decimal? TotalAwardedValue,
    bool CommercialValuesVisible);

public interface IGetGovernanceOverviewHandler
{
    Task<GovernanceOverviewDto> HandleAsync(CancellationToken ct);
}
