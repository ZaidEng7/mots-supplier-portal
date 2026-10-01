// Making sure only one supplier import, or one supplier push to the ERP, runs at a time.
//
// WHY A LOCK AT ALL. The hourly job and a person pressing "Run the import" can overlap, and so can two people.
// Both runs read the ERP, both find a supplier the portal does not hold yet, both create it - and the registry the
// ministry reads now has the same company twice under two reference codes, each looking correct on its own.
//
// THE PUSH HOLDS IT TOO. SupplierErpPushJob creates a supplier in the ERP and then saves the name the ERP gave it, and
// an import between the two would find that ERP supplier carried by nobody and create a second portal supplier from
// it. So a push and an import never overlap either, and an import refused while a push runs says so - an import or a
// push is running - rather than naming a run that does not exist.
//
// A DATABASE LOCK, NOT A LOCK IN MEMORY. A lock inside the application would only stop overlaps within one running
// copy of it, and the hourly job and a web request are not guaranteed to be the same copy. Postgres' own advisory
// lock is held by the connection, so it holds across every copy that shares the database - and it is released
// automatically if the connection dies, so a crashed run can never leave the import locked forever.
//
// TRY, DO NOT WAIT. A second run that queued behind the first would then do the same work again the moment the
// first finished. Refusing straight away, with a message that says an import or a push is running, is the honest
// answer. The hourly job, which nobody is watching, tries once more a couple of minutes later instead of losing its
// hour - see ErpSupplierSyncJob.
//
// THE CONNECTION MUST STAY OPEN for as long as the lock is held, because the lock belongs to it. The caller opens it
// before acquiring and closes it after releasing; everything in between - every supplier, every account - runs on
// that same connection, because the context and the identity store share it.

namespace MotsSupplierPortal.Infrastructure.Integration.Erp;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Infrastructure.Persistence;

internal static class ErpImportLock
{
    private const long Key = 7_346_815_201_001;

    public static async Task<bool> TryAcquireAsync(AppDbContext db, CancellationToken ct) =>
        await db.Database
            .SqlQuery<bool>($"SELECT pg_try_advisory_lock({Key}) AS \"Value\"")
            .SingleAsync(ct);

    public static async Task ReleaseAsync(AppDbContext db) =>
        await db.Database
            .SqlQuery<bool>($"SELECT pg_advisory_unlock({Key}) AS \"Value\"")
            .SingleAsync(CancellationToken.None);
}
