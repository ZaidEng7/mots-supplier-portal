// Reading what the ERP has and what the portal already has, then asking the rules what would happen.
//
// TWO READS AND NO WRITES. That is the whole of it - the rules live in ErpImportPreviewBuilder where they can be
// exercised without a database, and this is the boring half that fetches. It is written this way because the
// interesting failures in an import are decisions, not queries, and decisions behind a database are decisions
// nobody tests thoroughly.
//
// THE PORTAL SIDE IS PROJECTED, NOT LOADED. Eighty ERP suppliers matched against however many portal suppliers
// needs three columns, and loading whole aggregates to read three columns would pull addresses, documents,
// representatives and bank accounts through the change tracker for nothing.
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
// THE PREVIEW IS AUDITED AND THE AUDIT IS SAVED BEFORE THE REPORT IS BUILT. It reads a list of suppliers out of
// another ministry system on somebody's authority, which is worth a row whatever the outcome. Saving it first is
// deliberate: the three export routes in this product logged without saving for months and wrote nothing at all,
// and the shape of that bug was exactly this - a LogAsync with no SaveChangesAsync after it.

namespace MotsSupplierPortal.Infrastructure.Integration.Erp;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Integration;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class PreviewErpImportHandler(
    IErpSupplierSource source,
    AppDbContext db,
    IAuditLogger audit) : IPreviewErpImportHandler
{
    public async Task<ErpImportPreviewReport> HandleAsync(CancellationToken ct)
    {
        await audit.LogAsync(
            aggregateType: "Supplier",
            aggregateId: Guid.Empty,
            action: "ErpImportPreviewed",
            ct: ct);

        await db.SaveChangesAsync(ct);

        var existing = await db.Suppliers
            .AsNoTracking()
            .Select(s => new
            {
                s.ExternalId,
                s.ReferenceCode,
                TaxId = s.LegalInfo!.TaxId,
                AddressCount = s.Addresses.Count,
                s.OnboardingState,
            })
            .ToListAsync(ct);

        var byExternalId = existing
            .Where(s => s.ExternalId != null)
            .GroupBy(s => s.ExternalId!)
            .ToDictionary(
                group => group.Key,
                group => new ErpImportCandidateMatch(
                    group.First().ReferenceCode,
                    group.First().TaxId,
                    group.First().AddressCount,
                    Domain.Suppliers.Supplier.AllowsContactEdits(group.First().OnboardingState)
                        ? null
                        : $"in state '{group.First().OnboardingState}'"));

        var unlinkedByTaxId = existing
            .Where(s => s.ExternalId == null && s.TaxId != null)
            .GroupBy(s => s.TaxId!)
            .ToDictionary(group => group.Key, group => group.First().ReferenceCode);

        var registrationNumbers = await RegistrationNumbersInPortal.ReadAsync(db, ct);

        var erpSuppliers = await source.ListSuppliersAsync(ct);

        return ErpImportPreviewBuilder.Build(erpSuppliers, byExternalId, unlinkedByTaxId, registrationNumbers);
    }
}
