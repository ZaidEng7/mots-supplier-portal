// Reading what the ERP has and what the portal already has, then asking the rules what would happen.
//
// IT READS, SAVES ONE AUDIT ROW, AND CHANGES NO SUPPLIER. The reads are the portal's ERP-linked suppliers, through
// LinkedSuppliersInPortal, which the run reads through too; the tax numbers of suppliers with no ERP identifier; the
// registration numbers the portal holds; and the ERP's suppliers. Every decision lives in ErpImportPreviewBuilder and
// the rules it calls, where it can be exercised without a database, because the interesting failures in an import are
// decisions, not queries, and decisions behind a database are decisions nobody tests thoroughly.
//
// SUPPLIERS WITH NO TAX NUMBER ARE ABSENT FROM THE DUPLICATE INDEX rather than grouped under an empty key. The
// duplicate check asks "is this tax number already on a supplier that the ERP does not know about", and a null
// key would answer yes for every supplier that has no tax number at all - which is most of them today, and would
// bury the real collisions under a flag on every row.
//
// THE FIRST REFERENCE CODE WINS IF A TAX NUMBER IS SHARED. Nothing in the schema stops two portal suppliers
// carrying the same tax number, and that itself is worth a person's attention, but this preview's job is to name
// one and move on rather than to resolve it.
//
// THE PREVIEW IS AUDITED AND THE AUDIT IS SAVED BEFORE THE REPORT IS BUILT, and the row names the person who asked.
// It reads a list of suppliers out of another ministry system on somebody's authority, which is worth a row whatever
// the outcome, and a row with no actor does not say whose authority it was. A LogAsync with no SaveChangesAsync after
// it writes nothing, which is how the three export routes in this product once logged for months without recording
// anything.
//
// IT READS THE WHOLE REGISTRY ON PURPOSE, AND IT IS NOT ROW-SCOPED. It matches the ERP against every supplier the
// portal holds, and a view limited to one organisation would report every supplier it could not see as new. It reads
// the caller's scope only to name the person on the audit row. RowScopeGuardTests matches on that token, so it counts
// this handler as scoped and lists no exemption for it, as for RunErpImportHandler; that verdict is about attribution,
// not about which rows are read.

namespace MotsSupplierPortal.Infrastructure.Integration.Erp;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Integration;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class PreviewErpImportHandler(
    IErpSupplierSource source,
    AppDbContext db,
    IAuditLogger audit,
    IScopeContext scope) : IPreviewErpImportHandler
{
    public async Task<ErpImportPreviewReport> HandleAsync(CancellationToken ct)
    {
        await audit.LogAsync(
            aggregateType: "Supplier",
            aggregateId: Guid.Empty,
            action: "ErpImportPreviewed",
            actorUserId: scope.UserId,
            ct: ct);

        await db.SaveChangesAsync(ct);

        var byExternalId = await LinkedSuppliersInPortal.ReadForPreviewAsync(db, ct);

        var unlinked = await db.Suppliers
            .AsNoTracking()
            .Where(s => s.ExternalId == null)
            .Select(s => new { TaxId = s.LegalInfo!.TaxId, s.ReferenceCode })
            .ToListAsync(ct);

        var unlinkedByTaxId = unlinked
            .Where(s => s.TaxId != null)
            .GroupBy(s => s.TaxId!)
            .ToDictionary(group => group.Key, group => group.First().ReferenceCode);

        var registrationNumbers = await RegistrationNumbersInPortal.ReadAsync(db, ct);

        var erpSuppliers = await source.ListSuppliersAsync(ct);

        return ErpImportPreviewBuilder.Build(erpSuppliers, byExternalId, unlinkedByTaxId, registrationNumbers);
    }
}
