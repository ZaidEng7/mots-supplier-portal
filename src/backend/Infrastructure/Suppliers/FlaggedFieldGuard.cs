// While a reviewer has asked for more information, only the fields they flagged may be edited.
//
// This used to be enforced only by disabled inputs in the browser. Ten of the eleven profile mutation
// handlers had no check on the server, so a direct call edited any unflagged field, including the
// compliance-critical ones that send the application back for review.
//
// That is precisely what the written rules forbid: the interface may hide an affordance, it may never be the
// boundary that enforces it.
//
// The guard does nothing in every state except the one where information has been requested, so ordinary
// editing is unaffected.
//
// An open request that cannot be read is treated as a refusal rather than as an absence of restriction. No
// open request while in that state should not happen, and if it does, falling open is the wrong direction to
// fail.
//
// The multi-field form exists for the profile patch, which can carry several fields in one request: every
// field the caller actually set has to be flagged.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

internal static class FlaggedFieldGuard
{
    public static async Task<string?> RefusalReasonAsync(
        AppDbContext db, Supplier supplier, string fieldCode, CancellationToken ct)
    {
        if (supplier.OnboardingState != SupplierOnboardingState.InfoRequested)
        {
            return null;
        }

        var flagged = await db.SupplierReviewAnnotations
            .Where(a => a.SupplierId == supplier.Id && a.ResolvedAt == null)
            .OrderByDescending(a => a.RequestedAt)
            .Select(a => a.FlaggedProfileFields)
            .FirstOrDefaultAsync(ct);

        if (flagged is null)
        {
            return "No open information request found; this application is not currently editable.";
        }

        if (flagged.Contains(fieldCode, StringComparer.Ordinal))
        {
            return null;
        }

        return $"'{fieldCode}' was not flagged in the reviewer's information request. " +
               $"Editable fields: {string.Join(", ", flagged)}.";
    }

    public static async Task<string?> RefusalReasonAsync(
        AppDbContext db, Supplier supplier, IReadOnlyList<string> fieldCodes, CancellationToken ct)
    {
        foreach (var code in fieldCodes)
        {
            var refusal = await RefusalReasonAsync(db, supplier, code, ct);
            if (refusal is not null) return refusal;
        }

        return null;
    }
}
