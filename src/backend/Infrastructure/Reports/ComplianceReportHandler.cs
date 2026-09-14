// Builds the compliance report: supplier health and document health.
//
//
// THIS REPORT HAS NO ORGANIZATION DIMENSION
//
// That is a fact about the data rather than a gap here. A supplier belongs to no buying organization: the
// registry is one ministry-wide list of counterparties, and documents hang off the supplier.
//
// So there is no partition to scope these counts by, and adding a filter would mean inventing an ownership
// relation the schema does not have. The permission alone is the gate.
//
// Said plainly, because the alternative is worse. A scope condition that looks like row security and
// silently matches everything reads, to the next person, as a check that is already handled.

namespace MotsSupplierPortal.Infrastructure.Reports;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Reports;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class ComplianceReportHandler(AppDbContext db) : IComplianceReportHandler
{
    public async Task<ComplianceReportDto?> HandleAsync(CancellationToken ct)
    {
        var suppliersByState = await db.Suppliers.AsNoTracking()
            .GroupBy(s => s.LifecycleState)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var documents = db.SupplierDocuments.AsNoTracking().Where(d => d.IsLatestVersion);

        var documentsByState = await documents
            .GroupBy(d => d.State)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return new ComplianceReportDto(
            suppliersByState.Select(s => new ReportCountDto(s.State.ToString(), s.Count)).OrderBy(c => c.Key).ToList(),
            documentsByState.Select(d => new ReportCountDto(d.State.ToString(), d.Count)).OrderBy(c => c.Key).ToList(),
            suppliersByState.Sum(s => s.Count),
            documentsByState.Where(d => d.State == DocumentState.ExpiringSoon).Sum(d => d.Count),
            documentsByState.Where(d => d.State == DocumentState.Expired).Sum(d => d.Count));
    }
}
