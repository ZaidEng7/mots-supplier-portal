// The vocabulary for category coverage: one category, and how much of the market stands behind it.
//
// Counts only, like every other governance read. Nothing here names a supplier, a tender or a value, so
// there is no per-row filter for a later edit to forget. That also means this read is untouched by the
// commercial-visibility question, because no figure on it is commercial.
//
// Every active category appears, including the empty ones. The screen is about coverage, and a category no
// supplier has registered against is the finding. A list that omitted it would answer which categories have
// suppliers while looking like it answered which categories exist.
//
//
// WHY THERE ARE TWO SUPPLIER COUNTS
//
// The first is suppliers whose registration was approved and who list this category. That is the pool a
// buyer's invitations are drawn from.
//
// The second is those of them currently able to trade. Suspension is why the two differ, and a category
// whose only supplier is suspended reads as covered until the second number is shown beside the first.
//
//
// THE OTHER TWO FIGURES
//
// Catalogue entries in the category, across all suppliers. Keeping a catalogue is optional, so this measures
// how well the category is described rather than how many companies can serve it.
//
// Tenders that reached the market with at least one line in this category. Drafts and cancelled tenders are
// excluded, because they never asked the market for anything.

namespace MotsSupplierPortal.Application.Governance;

public sealed record CategoryCoverageDto(
    string CategoryCode,
    string NameAr,
    string NameEn,
    int ApprovedSuppliers,
    int ActiveSuppliers,
    int ActiveOfferings,
    int Tenders,
    int AwardedTenders);

public sealed record CategoryCoverageOverviewDto(
    IReadOnlyList<CategoryCoverageDto> Categories,
    int CategoriesWithNoActiveSupplier,
    bool CategoriesAreFlat);

public interface IGetCategoryCoverageHandler
{
    Task<CategoryCoverageOverviewDto> HandleAsync(CancellationToken ct);
}
