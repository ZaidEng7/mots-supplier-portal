namespace MotsSupplierPortal.Application.Governance;

/// <summary>
/// SCR-604: one category, and how much of the market is behind it.
///
/// <para><b>Counts only, like every other governance read.</b> BRULE-086 grants the Ministry
/// "aggregate/governance metrics only", so nothing here names a supplier, a tender or a value - there is no
/// per-row filter for a later edit to forget. That also means this read is unaffected by the
/// commercial-visibility question D-57 is waiting on a signature for: no figure here is commercial.</para>
///
/// <para><b>Every active category appears, including the empty ones.</b> The screen is about COVERAGE, and a
/// category no supplier has registered against is the finding - a list that omitted it would answer
/// "which categories have suppliers" while looking like it answered "which categories exist".</para>
/// </summary>
/// <param name="ApprovedSuppliers">Suppliers whose onboarding is Approved and who list this category. The
/// number a buyer's invitation pool is drawn from.</param>
/// <param name="ActiveSuppliers">Of those, the ones currently able to trade - Approved onboarding AND Active
/// lifecycle. Suspension is why the two differ, and a category whose only supplier is suspended reads as
/// covered until the second number is shown beside the first.</param>
/// <param name="ActiveOfferings">Catalogue entries in this category, across all suppliers. Recording a
/// catalogue is optional, so this is a measure of how well the category is DESCRIBED rather than of how many
/// companies can serve it.</param>
/// <param name="Tenders">Tenders that reached the market with at least one line item in this category.
/// Draft and cancelled tenders are excluded - they never asked the market for anything.</param>
/// <param name="AwardedTenders">Of those, the ones that ended in an award. A category with tenders and no
/// awards is the shape of a market that is being asked and not answering.</param>
public sealed record CategoryCoverageDto(
    string CategoryCode,
    string NameAr,
    string NameEn,
    int ApprovedSuppliers,
    int ActiveSuppliers,
    int ActiveOfferings,
    int Tenders,
    int AwardedTenders);

/// <summary>
/// SCR-604's whole response.
///
/// <para><see cref="CategoriesWithNoActiveSupplier"/> is computed here rather than left to the screen: it is
/// the one number a governance reader would otherwise have to count by eye, and counting by eye is how a
/// dashboard becomes decoration.</para>
/// </summary>
public sealed record CategoryCoverageOverviewDto(
    IReadOnlyList<CategoryCoverageDto> Categories,
    int CategoriesWithNoActiveSupplier,
    /// <summary>Stated on the response because the category list is flat by design (MSP-54's interim list,
    /// no parent), and SCR-604's own row says "category tree". A screen that drew a tree from a flat list
    /// would be inventing a hierarchy nobody has decided.</summary>
    bool CategoriesAreFlat);

public interface IGetCategoryCoverageHandler
{
    Task<CategoryCoverageOverviewDto> HandleAsync(CancellationToken ct);
}
