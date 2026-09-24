// The port behind the ministry's RFQ feed.
//
// It streams, like the supplier feed, so the whole set is never held in memory.
//
// Its rows are invitations rather than tenders or proposals, because the workbook asks for one row per
// supplier per tender and an invitation is precisely that pairing. The proposal, when there is one, is
// attached to the row rather than driving it.
//
// IT ALSO READS A PAGE, for the JSON representation, ordered by tender then supplier - the same order it
// streams in, and both halves immutable. That is what makes a page boundary safe: neither a reference code nor
// the pairing changes, so a row whose proposal is edited between two pages stays exactly where it was. The
// cursor therefore carries two parts rather than one, because the sort has two.
//
// A ROW'S MODIFIED TIME IS THE LATER OF ITS TWO HALVES, and getting this wrong is the most expensive mistake
// available in this feed. The row is an invitation crossed with a proposal, so it changes when either changes:
// filtering on the invitation alone would mean a supplier's quote could be submitted, corrected, or withdrawn
// and the ministry's nightly pull would never see it - the total they hold would be frozen at whatever it was
// the night the invitation was sent, with nothing anywhere reporting a problem.

namespace MotsSupplierPortal.Application.Rfqs;

public interface IMinistryRfqFeedHandler
{
    IAsyncEnumerable<MinistryRfqFeedRecord> StreamAsync(CancellationToken ct);

    Task<IReadOnlyList<MinistryRfqFeedRecord>> PageAsync(
        string? afterRfqReferenceCode,
        string? afterSupplierReferenceCode,
        DateTimeOffset? modifiedSince,
        int limit,
        CancellationToken ct);
}
