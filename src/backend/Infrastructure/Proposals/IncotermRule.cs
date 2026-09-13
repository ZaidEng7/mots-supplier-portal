// A delivery term on a bid must name a row in the reference table, or the bid is refused.
//
//
// WHAT THIS REPLACES
//
// The field was free text whose only guard was its length, so "ASAP", "fob" and a misspelling were all
// accepted and all printed into the comparison matrix beside the real terms.
//
// Two bids using different words for one term compared as different. Two using one word for different terms
// compared as the same.
//
//
// THREE DELIBERATE EDGES
//
// Case is normalised rather than refused. The standard's codes are upper-case and a supplier typing a term in
// lower case means that term; refusing it would be pedantry, while storing it as typed would put the free-text
// problem straight back. The stored value is always the table's own.
//
// Absent stays allowed. The field is optional in the contract and in the domain, and a domestic service
// contract may legitimately quote no delivery term at all. This rule governs what a PRESENT value may be.
//
// Inactive counts as unknown. A ministry that deactivates a term has decided bids may not quote it, and
// historical bids keep theirs because nothing rewrites a stored bid.
//
// The refusal carries the list a bidder needs in order to correct it.

namespace MotsSupplierPortal.Infrastructure.Proposals;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Infrastructure.Persistence;

internal static class IncotermRule
{
    internal static async Task<(bool Known, string? Code)> ResolveAsync(AppDbContext db, string? submitted, CancellationToken ct)
    {
        var trimmed = submitted?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return (true, null);

        var upper = trimmed.ToUpperInvariant();
        var known = await db.Incoterms.AsNoTracking().AnyAsync(i => i.Code == upper && i.IsActive, ct);
        return known ? (true, upper) : (false, null);
    }

    internal static async Task<string> RefusalDetailAsync(AppDbContext db, string? submitted, CancellationToken ct)
    {
        var offered = await db.Incoterms.AsNoTracking()
            .Where(i => i.IsActive).OrderBy(i => i.Code).Select(i => i.Code).ToListAsync(ct);

        return $"'{submitted}' is not a delivery term this portal accepts. Use one of: {string.Join(", ", offered)}.";
    }
}
