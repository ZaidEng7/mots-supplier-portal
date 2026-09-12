using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Proposals;

/// <summary>
/// T-072: a delivery term on a bid names a row in the Incoterm table, or the bid is refused.
///
/// <para><b>What this replaces.</b> <c>Proposal.IncotermCode</c> was a free <c>varchar(10)</c> whose
/// only guard was its length, so "ASAP", "fob" and "FOP" were all accepted and all printed into the
/// comparison matrix beside the real terms. Two bids using different words for one term compared as
/// different; two using one word for different terms compared as the same.</para>
///
/// <para><b>Case is normalised rather than refused.</b> The standard's codes are upper-case and a
/// supplier typing "fob" means FOB - refusing that would be pedantry, while storing it would put the
/// free-text problem straight back. The stored value is always the table's own.</para>
///
/// <para><b>Absent stays allowed.</b> The field is optional in §12.5 and in the aggregate, and a
/// domestic service contract may legitimately quote no Incoterm at all. This rule governs what a
/// PRESENT value may be.</para>
///
/// <para><b>Inactive is unknown.</b> A ministry that deactivates a term has decided bids may not
/// quote it; historical proposals keep theirs, because nothing rewrites a stored bid.</para>
/// </summary>
internal static class IncotermRule
{
    /// <summary>
    /// The stored form of a submitted code, or null when the caller sent none.
    ///
    /// <para>Returns <c>false</c> with a null code when the value names no active row - the caller
    /// turns that into the refusal its own contract speaks.</para>
    /// </summary>
    internal static async Task<(bool Known, string? Code)> ResolveAsync(AppDbContext db, string? submitted, CancellationToken ct)
    {
        var trimmed = submitted?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return (true, null);

        var upper = trimmed.ToUpperInvariant();
        var known = await db.Incoterms.AsNoTracking().AnyAsync(i => i.Code == upper && i.IsActive, ct);
        return known ? (true, upper) : (false, null);
    }

    /// <summary>The refusal's detail, with the list a bidder needs to correct it.</summary>
    internal static async Task<string> RefusalDetailAsync(AppDbContext db, string? submitted, CancellationToken ct)
    {
        var offered = await db.Incoterms.AsNoTracking()
            .Where(i => i.IsActive).OrderBy(i => i.Code).Select(i => i.Code).ToListAsync(ct);

        return $"'{submitted}' is not a delivery term this portal accepts. Use one of: {string.Join(", ", offered)}.";
    }
}
