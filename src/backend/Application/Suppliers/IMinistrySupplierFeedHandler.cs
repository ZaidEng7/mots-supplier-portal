// The port behind the ministry's supplier feed.
//
// It streams, like the registry export, so the whole registry is never held in memory and the download starts
// immediately.
//
// It loads LESS than the registry export does, and that is why it is a separate port rather than a second
// reader of the same one. This feed's twenty-two columns need a supplier, its primary address, its primary
// representative and its categories. They do not need documents, offerings, review annotations or profile
// completeness - which the registry export loads per supplier, at four extra queries each. Reusing that
// handler would have paid for all of it and thrown it away.

namespace MotsSupplierPortal.Application.Suppliers;

public interface IMinistrySupplierFeedHandler
{
    Task<(IReadOnlyDictionary<string, string> CategoryNameEn, IReadOnlyDictionary<string, string> RegionNameEn)>
        GetLookupsAsync(CancellationToken ct);

    IAsyncEnumerable<MinistrySupplierFeedRecord> StreamAsync(CancellationToken ct);
}
