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
//
// IT ALSO READS A PAGE, for the JSON representation. Streaming is right for a file download, where the caller
// takes everything and the point is to start writing immediately; paging is right for a nightly job, whose own
// requirements ask for it. The two coexist rather than one replacing the other.
//
// A PAGE IS ORDERED BY REFERENCE CODE, which is the same order the stream uses and is what makes paging safe. A
// reference code never changes once allocated, so a supplier edited between two pages cannot move position:
// ordering by anything mutable - a last-modified time, most obviously - would let a row that was updated
// mid-walk jump ahead of a page boundary and be skipped, or behind it and be sent twice.
//
// MODIFIEDSINCE IS A FILTER, NOT AN ORDER, which is why it can be applied to that same stable ordering. It
// reads Supplier.UpdatedAt, which the persistence layer stamps whenever the aggregate's version advances -
// including a write to an address, a representative or a bank account. That matters more than it sounds: the
// columns the ministry is waiting on are mostly on the address, so a filter that only noticed edits to the
// supplier row itself would silently never re-send a corrected coordinate.

namespace MotsSupplierPortal.Application.Suppliers;

public interface IMinistrySupplierFeedHandler
{
    Task<(IReadOnlyDictionary<string, string> CategoryNameEn, IReadOnlyDictionary<string, string> RegionNameEn)>
        GetLookupsAsync(CancellationToken ct);

    IAsyncEnumerable<MinistrySupplierFeedRecord> StreamAsync(CancellationToken ct);

    Task<IReadOnlyList<MinistrySupplierFeedRecord>> PageAsync(
        string? afterReferenceCode, DateTimeOffset? modifiedSince, int limit, CancellationToken ct);
}
