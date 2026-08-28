using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>
/// MSP-77 / STORY-03.3.1 AC1: while a supplier is in <c>InfoRequested</c>, only the fields the
/// reviewer flagged are editable.
///
/// This was previously enforced ONLY by `disabled` attributes in the browser
/// (OnboardingPage.tsx `fieldEditable`). Ten of eleven profile mutation handlers had no
/// server-side check, so a direct API call edited any non-flagged field - including
/// compliance-critical ones that re-trigger review. That is precisely what BRULE-094 and
/// NFR-SEC-012 forbid: the UI may hide affordances, it may never be the security boundary.
///
/// The guard is a no-op in every state except InfoRequested, so ordinary editing is unaffected.
/// </summary>
internal static class FlaggedFieldGuard
{
    /// <summary>Null when the mutation is permitted; otherwise the refusal reason.</summary>
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

        // No open annotation while InfoRequested shouldn't happen, but if it does, refuse rather
        // than fall open - an unreadable restriction is not the same as no restriction.
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

    /// <summary>Multi-field variant for the core-profile PATCH, which can carry several fields in
    /// one request: every field the caller actually set must be flagged.</summary>
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
