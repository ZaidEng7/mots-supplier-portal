using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>
/// Shared document-requirement evaluation used by both the submit gate (STORY-03.1.1, requires
/// every required DocumentType to have a satisfying latest version) and the reviewer approval
/// gate (STORY-03.2.1, product-owner decision 2026-08-26: only blocks on Rejected/ScanRejected/
/// Expired, does not require every document to already be individually Approved).
/// </summary>
public static class DocumentCompletenessEvaluator
{
    public static async Task<IReadOnlyList<string>> GetMissingRequiredDocumentTypeCodesAsync(AppDbContext db, Guid supplierId, CancellationToken ct)
    {
        var requiredTypes = await db.DocumentTypes.Where(t => t.IsRequired && t.IsActive).ToListAsync(ct);
        if (requiredTypes.Count == 0) return [];

        var latestBySupplier = await db.SupplierDocuments
            .Where(d => d.SupplierId == supplierId && d.IsLatestVersion)
            .ToListAsync(ct);

        var missing = new List<string>();
        foreach (var type in requiredTypes)
        {
            var latest = latestBySupplier.FirstOrDefault(d => d.DocumentTypeId == type.Id);
            if (latest is null || !latest.SatisfiesSubmitRequirement)
            {
                missing.Add(type.Code);
            }
        }
        return missing;
    }

    public static async Task<IReadOnlyList<string>> GetBlockingRequiredDocumentTypeCodesAsync(AppDbContext db, Guid supplierId, CancellationToken ct)
    {
        var requiredTypes = await db.DocumentTypes.Where(t => t.IsRequired && t.IsActive).ToListAsync(ct);
        if (requiredTypes.Count == 0) return [];

        var latestBySupplier = await db.SupplierDocuments
            .Where(d => d.SupplierId == supplierId && d.IsLatestVersion)
            .ToListAsync(ct);

        var blocking = new List<string>();
        foreach (var type in requiredTypes)
        {
            var latest = latestBySupplier.FirstOrDefault(d => d.DocumentTypeId == type.Id);
            if (latest is not null && latest.BlocksApplicationApproval)
            {
                blocking.Add(type.Code);
            }
        }
        return blocking;
    }

    /// <summary>
    /// BRULE-018 (MSP-68): the required document types whose latest version is Rejected or Expired,
    /// which flag the supplier's profile incomplete until replaced with an approved version.
    ///
    /// <para>This is the rule that had NO behaviour at all. The two evaluators above are consulted
    /// only at the submit gate and the approval gate - both pre-approval - so a document expiring on
    /// an already-approved supplier changed nothing anywhere. The expiry job moved the document to
    /// Expired and the supplier's profile went on looking complete indefinitely.</para>
    ///
    /// <para><b>Rejected or Expired only, deliberately.</b> Not ExpiringSoon: BRULE-018 names those
    /// two states, and a document still valid for three weeks has not stopped satisfying anything.
    /// Flagging it would make "incomplete" the normal condition of every supplier with a document
    /// approaching renewal, which is how a warning stops being read.</para>
    ///
    /// <para>Computed rather than stored. A persisted flag would need updating from the expiry job,
    /// the review handlers and the upload path, and would be wrong the moment one of them forgot -
    /// which is the shape of defect this codebase keeps finding. Computed from the documents
    /// themselves, it cannot drift from them.</para>
    /// </summary>
    public static async Task<IReadOnlyList<string>> GetProfileIncompleteDocumentTypeCodesAsync(
        AppDbContext db, Guid supplierId, CancellationToken ct)
    {
        var requiredTypes = await db.DocumentTypes.Where(t => t.IsRequired && t.IsActive).ToListAsync(ct);
        if (requiredTypes.Count == 0) return [];

        var latestBySupplier = await db.SupplierDocuments
            .Where(d => d.SupplierId == supplierId && d.IsLatestVersion)
            .ToListAsync(ct);

        return
        [
            .. requiredTypes
                .Where(type => latestBySupplier
                    .FirstOrDefault(d => d.DocumentTypeId == type.Id)?.FlagsProfileIncomplete == true)
                .Select(type => type.Code)
        ];
    }
}
