// The one answer to "has this supplier sent the documents we require of them?"
//
// Three gates ask it: the supplier's submit gate, their resubmit gate, and the reviewer's approval gate.
// All three now ask the same question rather than the approval gate using a second, looser test of its
// own. The written rule's text is authoritative and unchanged; the code moved to meet it.
//
// A recorded product decision still holds exactly: approval does not require every document to be
// individually approved, because a document waiting on a reviewer still satisfies the submit requirement.
// What that decision never covered, a MISSING document and an UNSCANNED one, is now blocked.
//
// The looser version of the approval gate let both through. The decision said approval must not require
// every document to already be approved. It did not say approval should not require them to be present.
// Those are different claims and only the first was decided, so this implements the first and nothing
// wider.
//
//
// THE FOUR NUMBERS IN THE SUMMARY DO NOT SUM
//
// They are counted over the LATEST version of each required type, because that is the version that decides
// anything. A rejected version superseded by an approved one is history, and counting it would tell a
// supplier they still have a problem they fixed.
//
// A required type with nothing uploaded appears in none of the three counts, so the shortfall is the
// required total less the other three. An expiring or expired document is in none of them either: it was
// approved once and is not now, which is exactly the case that re-opens an approved supplier's profile.
//
// Uploaded and under-review are one number to a supplier, because they have sent it and are waiting. A
// document waiting on the virus scanner belongs there too: the file is in, and the wait happens to be on
// the scanner rather than on a reviewer. A file the scanner refused does not, because nothing is waiting on
// anybody and the supplier has to send another one.
//
//
// THE RULE THAT HAD NO BEHAVIOUR AT ALL
//
// The two gates above are consulted only before approval, so a document expiring on an already-approved
// supplier changed nothing anywhere. The expiry job moved it to expired and the supplier's profile went on
// looking complete indefinitely.
//
// The last evaluator is that missing rule: the required types whose latest version is rejected or expired,
// which mark the profile incomplete until they are replaced.
//
// Rejected or expired only, deliberately. Not expiring-soon: the written rule names those two states, and
// a document still valid for three weeks has not stopped satisfying anything. Flagging it would make
// "incomplete" the normal condition of every supplier with a renewal approaching, which is how a warning
// stops being read.
//
// It is computed rather than stored. A stored flag would need updating from the expiry job, the review
// handlers and the upload path, and would be wrong the moment one of them forgot, which is the shape of
// defect this codebase keeps finding. Computed from the documents themselves, it cannot drift from them.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

public static class DocumentCompletenessEvaluator
{
    public static async Task<DocumentsSummaryDto> GetDocumentsSummaryAsync(AppDbContext db, Guid supplierId, CancellationToken ct)
    {
        var requiredTypes = await RequiredDocumentTypeResolver.ForSupplierAsync(db, supplierId, ct);
        if (requiredTypes.Count == 0) return new DocumentsSummaryDto(0, 0, 0, 0);

        var latestBySupplier = await db.SupplierDocuments
            .Where(d => d.SupplierId == supplierId && d.IsLatestVersion)
            .ToListAsync(ct);

        var approved = 0;
        var pending = 0;
        var rejected = 0;

        foreach (var type in requiredTypes)
        {
            var latest = latestBySupplier.FirstOrDefault(d => d.DocumentTypeId == type.Id);
            if (latest is null) continue;

            switch (latest.State)
            {
                case DocumentState.Approved:
                    approved++;
                    break;
                case DocumentState.PendingScan:
                case DocumentState.Uploaded:
                case DocumentState.UnderReview:
                    pending++;
                    break;
                case DocumentState.Rejected:
                case DocumentState.ScanRejected:
                    rejected++;
                    break;
                default:
                    break;
            }
        }

        return new DocumentsSummaryDto(requiredTypes.Count, approved, pending, rejected);
    }

    public static async Task<IReadOnlyList<string>> GetMissingRequiredDocumentTypeCodesAsync(AppDbContext db, Guid supplierId, CancellationToken ct)
    {
        var requiredTypes = await RequiredDocumentTypeResolver.ForSupplierAsync(db, supplierId, ct);
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
        var requiredTypes = await RequiredDocumentTypeResolver.ForSupplierAsync(db, supplierId, ct);
        if (requiredTypes.Count == 0) return [];

        var latestBySupplier = await db.SupplierDocuments
            .Where(d => d.SupplierId == supplierId && d.IsLatestVersion)
            .ToListAsync(ct);

        var blocking = new List<string>();
        foreach (var type in requiredTypes)
        {
            var latest = latestBySupplier.FirstOrDefault(d => d.DocumentTypeId == type.Id);

            if (latest is null || !latest.SatisfiesSubmitRequirement)
            {
                blocking.Add(type.Code);
            }
        }
        return blocking;
    }

    public static async Task<IReadOnlyList<string>> GetProfileIncompleteDocumentTypeCodesAsync(
        AppDbContext db, Guid supplierId, CancellationToken ct)
    {
        var requiredTypes = await RequiredDocumentTypeResolver.ForSupplierAsync(db, supplierId, ct);
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
