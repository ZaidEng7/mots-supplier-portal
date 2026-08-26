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
}
