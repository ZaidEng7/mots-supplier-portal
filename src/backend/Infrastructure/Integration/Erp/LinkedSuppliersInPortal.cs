// The portal's ERP-linked suppliers, as the preview and the run both read them before the first write.
//
// ONE QUERY FOR BOTH, because the plan the preview forecasts from and the plan the run acts on must be built from the
// same columns: a column added to one copy and not the other would leave the forecast quietly wrong. ReadAsync gives
// ErpSyncPlan what it needs, for the run. ReadForPreviewAsync is the same read with what only the preview reports
// added - how many addresses a supplier has, whether its details may be edited, and whether an award-critical
// document that expired is waiting for an approved renewal - because the run asks those per supplier as it updates.
//
// PROJECTED, NOT LOADED. Matching eighty ERP suppliers needs a handful of columns, and loading whole aggregates would
// pull addresses, documents, representatives and bank accounts through the change tracker for nothing.
//
// THE SIGN-IN ADDRESS IS THE PRIMARY REPRESENTATIVE'S LOGIN, or the first linked one's, because ErpSyncPlan compares
// logins to spot a probable rename: the login is unique in this product and the contact email is not.
//
// ONE ROW PER ERP IDENTIFIER, AND TWO SUPPLIERS SHARING ONE ARE NOT SUPPORTED. Nothing in the schema stops it, but the
// import assumes it never happens: this keeps whichever row the database returns first, which is not guaranteed to be
// the same row for the preview and the run, and the run's suspensions and marks act on every supplier that carries
// the identifier.
//
// IT READS THE WHOLE REGISTRY, not the caller's rows, because a scoped view would report every supplier it could not
// see as gone and the run would suspend them. It is not a handler, so RowScopeGuardTests does not scan it; the two
// handlers that call it are accounted for there.
//
// EVERY FIELD IS PASSED BY NAME. PortalLinkedSupplier and ErpImportCandidateMatch give most fields a default for the
// tests' sake, so a field added to either compiles without being read here, and silently takes its default.

namespace MotsSupplierPortal.Infrastructure.Integration.Erp;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Integration;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Suppliers;

internal static class LinkedSuppliersInPortal
{
    public static async Task<IReadOnlyList<PortalLinkedSupplier>> ReadAsync(AppDbContext db, CancellationToken ct) =>
        [.. (await ReadRowsAsync(db, ct)).Select(row => row.Linked)];

    public static async Task<IReadOnlyDictionary<string, ErpImportCandidateMatch>> ReadForPreviewAsync(
        AppDbContext db, CancellationToken ct)
    {
        var rows = await ReadRowsAsync(db, ct);

        var awaitingRenewal = await AwardCriticalRenewal.SuppliersAwaitingRenewalAsync(
            db, [.. rows.Select(row => row.Id)], ct);

        return rows.ToDictionary(
            row => row.Linked.ExternalId,
            row => new ErpImportCandidateMatch(
                ReferenceCode: row.Linked.ReferenceCode,
                TaxId: row.Linked.TaxId,
                Name: row.Linked.Name,
                IsActive: row.Linked.IsActive,
                LoginEmail: row.Linked.LoginEmail,
                SuspendedAsRemovedFromErp: row.Linked.SuspendedAsRemovedFromErp,
                MarkedRemovedFromErp: row.Linked.MarkedRemovedFromErp,
                AddressCount: row.AddressCount,
                BlockedByState: row.BlockedByState,
                ErpDisabledState: row.Linked.ErpDisabledState,
                AwaitsDocumentRenewal: awaitingRenewal.Contains(row.Id)),
            StringComparer.Ordinal);
    }

    private sealed record Row(Guid Id, PortalLinkedSupplier Linked, int AddressCount, string? BlockedByState);

    private static async Task<List<Row>> ReadRowsAsync(AppDbContext db, CancellationToken ct)
    {
        var rows = await db.Suppliers
            .AsNoTracking()
            .Where(s => s.ExternalId != null)
            .Select(s => new
            {
                s.Id,
                s.ExternalId,
                s.ReferenceCode,
                s.DisplayNameEn,
                TaxId = s.LegalInfo!.TaxId,
                LoginEmail = s.Representatives
                    .Where(r => r.UserId != null)
                    .OrderByDescending(r => r.IsPrimary)
                    .ThenBy(r => r.Id)
                    .Select(r => db.Users.Where(u => u.Id == r.UserId).Select(u => u.Email).FirstOrDefault())
                    .FirstOrDefault(),
                s.LifecycleState,
                s.SyncStatus,
                s.ErpDisabledState,
                AddressCount = s.Addresses.Count,
                s.OnboardingState,
            })
            .ToListAsync(ct);

        return [.. rows
            .GroupBy(r => r.ExternalId!, StringComparer.Ordinal)
            .Select(group => group.First())
            .Select(r => new Row(
                Id: r.Id,
                Linked: new PortalLinkedSupplier(
                    ExternalId: r.ExternalId!,
                    ReferenceCode: r.ReferenceCode,
                    Name: r.DisplayNameEn,
                    TaxId: r.TaxId,
                    LoginEmail: r.LoginEmail,
                    IsActive: r.LifecycleState == SupplierLifecycleState.Active,
                    SuspendedAsRemovedFromErp: r.SyncStatus == SupplierSyncStatus.RemovedFromErp,
                    MarkedRemovedFromErp: r.SyncStatus == SupplierSyncStatus.MarkedRemovedFromErp,
                    ErpDisabledState: r.ErpDisabledState),
                AddressCount: r.AddressCount,
                BlockedByState: Supplier.AllowsContactEdits(r.OnboardingState)
                    ? null
                    : $"in state '{r.OnboardingState}'"))];
    }
}
