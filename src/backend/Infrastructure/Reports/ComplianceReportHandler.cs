using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Reports;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Reports;

/// <summary>
/// FEAT-19.2: compliance reporting - supplier health and document health.
///
/// <para><b>This report has no organization dimension, and that is a fact about the data model
/// rather than a gap in this handler.</b> <c>Supplier</c> carries no <c>OrganizationId</c>: the
/// supplier registry is a single ministry-wide list of counterparties, not a per-organization one,
/// and <c>SupplierDocument</c> hangs off the supplier. So there is no partition to scope these
/// counts by, and adding a filter would mean inventing an ownership relation the schema does not
/// have. The gate is therefore the permission alone.</para>
///
/// <para>Said plainly because the alternative is worse: a scope predicate that looks like row
/// security and silently matches everything reads, to the next person, as a check that is already
/// handled. If suppliers ever become organization-owned, this handler needs revisiting and this
/// comment is the marker.</para>
///
/// <para><b>A read over the expiry job's state, not a second expiry calculation.</b> The daily job
/// moves documents Approved -> ExpiringSoon -> Expired. Recomputing "is this expiring" here from
/// ExpiryDate would be a second implementation of the same rule, and the two would eventually
/// disagree - with the report being the one nobody checks against reality.</para>
/// </summary>
public sealed class ComplianceReportHandler(AppDbContext db) : IComplianceReportHandler
{
    public async Task<ComplianceReportDto?> HandleAsync(CancellationToken ct)
    {
        var suppliersByState = await db.Suppliers.AsNoTracking()
            .GroupBy(s => s.LifecycleState)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // Latest versions only. Superseded versions of a document are still rows, and counting them
        // would report a supplier who replaced an expiring certificate as still having one - the
        // report would show a compliance problem that the supplier has already fixed.
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
