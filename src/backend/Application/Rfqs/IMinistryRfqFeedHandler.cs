// The port behind the ministry's RFQ feed.
//
// It streams, like the supplier feed, so the whole set is never held in memory.
//
// Its rows are invitations rather than tenders or proposals, because the workbook asks for one row per
// supplier per tender and an invitation is precisely that pairing. The proposal, when there is one, is
// attached to the row rather than driving it.

namespace MotsSupplierPortal.Application.Rfqs;

public interface IMinistryRfqFeedHandler
{
    IAsyncEnumerable<MinistryRfqFeedRecord> StreamAsync(CancellationToken ct);
}
