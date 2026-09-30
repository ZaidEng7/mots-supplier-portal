// Creating in the ERP the suppliers that registered in the portal, once a reviewer has approved them.
//
// WHAT ONE RUN DOES. It takes the approved suppliers whose push is Requested or Linked and due, oldest request first,
// a few at a time, and makes the ERP colleague's calls for each, in his order: the Supplier record, its address, its
// contact, a website user, that user added to the Supplier's portal users, and the contact pointed at the user. The
// ERP's name for the new supplier becomes the portal's ExternalId, and from then on the hourly import matches the
// supplier by it like any other ERP supplier. While the ERP holds it in Draft, the import keeps the portal supplier
// suspended, and it releases the supplier once the ERP approves it. That is the import's own rule, unchanged; nothing
// here decides whether a supplier may trade.
//
// TWO THINGS START IT. ApproveApplicationHandler enqueues PushAsync for the supplier it has just approved, after its
// commit, so a supplier reaches the ERP moments after approval. RunAsync is the sweep every five minutes. It retries
// the attempts that failed a while ago and picks up any supplier the first run missed.
//
// NOTHING HAPPENS, QUIETLY, UNLESS THE CONNECTION IS ENABLED, ITS WRITE SWITCH IS ON AND IT NAMES A DEFAULT GROUP. A
// deployment that has not turned the push on is not failing every five minutes. ErpSupplierRegistrar also refuses
// every write while the switch is off, whoever calls it. A switch that is on with no group is logged as a warning,
// because the ERP refuses a supplier without one.
//
// IT HOLDS THE IMPORT'S LOCK FOR THE WHOLE RUN. Between the ERP creating a supplier and the portal saving its name, an
// import would find an ERP supplier that no portal supplier carries, and it would create a second one with an account
// of its own. Holding ErpImportLock from before the first call until after the last save means an import can never run
// in that gap. When the lock is taken, because an import or another push is running, the run steps aside quietly and
// the next one picks the suppliers up. It checks for due work before it takes the lock, so a run with nothing to do
// never keeps an import out.
//
// THE ERP MAKES A SECOND SUPPLIER FOR A SECOND REQUEST, so the Supplier record is never posted twice. Before the post,
// the attempt is marked as under way and saved (BeginErpPush). The name the ERP returns is saved at once, in a save of
// its own and before any other call (RecordErpSupplierCreated), so a failure after that point resumes from the name.
// An attempt that cannot rule out an earlier create looks in the ERP before it posts. That is a supplier whose
// in-flight marker is still set, one whose earlier attempt failed, and a create that got no answer or that the ERP
// says clashes with a record it already has. It looks by tax number, then for a Supplier with this name that the
// portal's own API user created since the push was requested. An ERP supplier that another portal supplier already
// carries is not a match. One match is linked instead of created. Several are for a person to settle, and the push
// fails naming them.
//
// LOOKING WHEN THE LAST ATTEMPT FAILED, NOT ONLY WHILE THE MARKER IS SET, is deliberate. RecordErpPushAttemptFailed and
// FailErpPush clear the marker, and the attempt that timed out on its create may have left one the ERP finished after
// the portal stopped waiting. The last error stays on the supplier through a retry and a new approval, so it is the
// record that an earlier attempt got somewhere.
//
// A PUSH THAT STOPPED PART-WAY RESUMES WITHOUT REPEATING ITSELF. The address, the contact and the user are each looked
// for before they are made. The address is matched by its first line and city among the Supplier's addresses, the
// contact by its email among the Supplier's contacts, and the user by email. With no email to match, any contact the
// Supplier has counts as its contact. The portal users are read and appended to, never replaced. The contact is pointed
// at the user only when the contact was made before the user, as the colleague's sixth call says, because the ERP links
// a new contact to an existing user with the same email by itself.
//
// WHAT A FAILURE DOES DEPENDS ON ITS KIND, as ErpFailure sorts it:
//
//   Permission     the credential is refused. That fails every supplier alike, so the run stops, no failure is
//                  recorded on any supplier, and an error is logged for somebody to fix the credential or its rights.
//                  A marker saved before a refused post stays, which only makes the next attempt look before it posts.
//   Permanent      the ERP refused something it will refuse again. The push fails with the ERP's own message and waits
//                  for a person.
//   anything else  a passing failure, including a child create that got no answer. The attempt is counted and the next
//                  one waits longer; see WaitAfter. The eighth failure in a row fails the push.
//
// A payload that ErpSupplierPayload holds fails the push with the builder's reasons, and nothing is written to the ERP.
// A switch turned off, or a connection disabled, while a run is under way stops the run quietly.
//
// EVERY WRITE TO THE SUPPLIER ROW IS A TARGETED UPDATE of the push's own columns and ExternalId, not a save of the whole
// record. The reviewer who approved a supplier is usually still on its page when the push runs a moment later, and the
// supplier may be editing its profile. A save through the record would move its version three times in a few seconds,
// and each of their next saves would be refused as stale. A save through the record could also be refused by theirs,
// and a refused save after the ERP has created the supplier is the gap this job exists to close. The domain's methods
// still decide every move, on a copy of the supplier read without tracking, and the update writes what they decided,
// and only while the push is where this run found it. The cost is that the version does not move either, so a client
// holding the reviewer view may be told it has not changed until the next save of the supplier; the next import makes
// one.
//
// EACH OUTCOME IS ON THE SUPPLIER'S AUDIT TRAIL, written in the same transaction as its update, with the system as the
// actor: created, completed, an attempt failed, failed. The marker saved before the post has no row, because it is
// bookkeeping. The row after it says what the post did.

namespace MotsSupplierPortal.Infrastructure.Integration.Erp;

using System.Text.Json.Nodes;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Integration;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class SupplierErpPushJob(
    AppDbContext db,
    IErpConnectionProvider connections,
    IErpSupplierRegistrar registrar,
    IAuditLogger audit,
    ILogger<SupplierErpPushJob> logger)
{
    public const string JobId = "supplier-erp-push";

    // A FEW SUPPLIERS PER RUN, AND ONE RUN AT A TIME. Each ERP call may take thirty seconds, so a batch of five keeps a
    // run against an ERP that stops answering part-way within the five minutes between sweeps. The rest wait for the
    // next sweep. A second run of the same kind waits up to five minutes for the first before the scheduler gives up on
    // it, and ErpImportLock keeps a push and an import apart whatever started them.
    public const int BatchSize = 5;
    public const int MaxAttempts = 8;
    private const int OneRunAtATimeSeconds = 5 * 60;
    private const string SystemActor = "system";

    // HOW FAR BACK A LOOK FOR AN EARLIER CREATE REACHES. The ERP stores a record's creation time by its own clock, and
    // that clock and the portal's differ, so the look starts fifteen minutes before the push was requested.
    public static readonly TimeSpan LookBack = TimeSpan.FromMinutes(15);

    // The sweep, and the push of one supplier straight after its approval.
    //
    // NO SCHEDULER RETRIES. The job keeps its own count on each supplier and decides when to try again. A retry by the
    // scheduler would repeat a run whose failures are already counted.
    [DisableConcurrentExecution(OneRunAtATimeSeconds)]
    [AutomaticRetry(Attempts = 0)]
    public Task RunAsync(CancellationToken ct = default) => RunForAsync(null, ct);

    [DisableConcurrentExecution(OneRunAtATimeSeconds)]
    [AutomaticRetry(Attempts = 0)]
    public Task PushAsync(Guid supplierId, CancellationToken ct = default) => RunForAsync(supplierId, ct);

    // The wait before the next attempt after this many failed ones in a row: one minute, then five, then fifteen, then
    // an hour each time. The first waits are short because most failures are the ERP restarting or a moment's network
    // trouble. After that the failure is clearly more than a moment, and eight attempts span about four and a half hours
    // before a person is asked.
    public static TimeSpan WaitAfter(int failedAttempts) => failedAttempts switch
    {
        <= 1 => TimeSpan.FromMinutes(1),
        2 => TimeSpan.FromMinutes(5),
        3 => TimeSpan.FromMinutes(15),
        _ => TimeSpan.FromHours(1),
    };

    private sealed record Fields(ErpRecordFields Supplier, ErpRecordFields Address, ErpRecordFields Contact);

    private sealed record PushColumns(SupplierErpPushStatus Status, string? ExternalId);

    private sealed record EarlierCreate(string? Name, string? How, string? Ambiguity);

    // One run, for every due supplier or for one. The gate, the look for due work and the lock come first, in that
    // order, so the lock is only taken when there is something to push.
    private async Task RunForAsync(Guid? only, CancellationToken ct)
    {
        var connection = await connections.CurrentAsync(ct);

        if (connection is not { IsEnabled: true, CreateSuppliersInErp: true })
        {
            logger.LogInformation(
                "Supplier push to the ERP skipped: no ERP connection is enabled with supplier creation switched on.");
            return;
        }

        if (string.IsNullOrWhiteSpace(connection.DefaultSupplierGroup))
        {
            logger.LogWarning(
                "Supplier push to the ERP skipped: supplier creation is switched on but no default ERP supplier group is set.");
            return;
        }

        var now = DateTimeOffset.UtcNow;

        if (!await Due(only, now).AnyAsync(ct)) return;

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            if (!await ErpImportLock.TryAcquireAsync(db, ct))
            {
                logger.LogInformation(
                    "Supplier push to the ERP skipped: an import or another push holds the ERP lock; the next run picks the "
                    + "suppliers up.");
                return;
            }

            try
            {
                await PushDueAsync(connection.DefaultSupplierGroup, only, now, ct);
            }
            finally
            {
                await ReleaseQuietlyAsync();
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    // The ERP's field lists are read once for the run, before the first supplier. A failure to read them is not about
    // any one supplier, but every due supplier would have met it, so each counts it as a failed attempt; a refused
    // credential stops the run like any other.
    private async Task PushDueAsync(string group, Guid? only, DateTimeOffset now, CancellationToken ct)
    {
        var due = await Due(only, now)
            .OrderBy(s => s.ErpPushRequestedAt)
            .ThenBy(s => s.Id)
            .Select(s => s.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (due.Count == 0) return;

        Fields fields;
        try
        {
            fields = new Fields(
                await registrar.ReadFieldsAsync(ErpRecordType.Supplier, ct),
                await registrar.ReadFieldsAsync(ErpRecordType.Address, ct),
                await registrar.ReadFieldsAsync(ErpRecordType.Contact, ct));
        }
        catch (ErpRequestException refusal) when (refusal.PushKind == ErpPushFailureKind.Permission)
        {
            LogPermissionStop(refusal);
            return;
        }
        catch (ErpRequestException refusal)
        {
            foreach (var supplierId in due)
            {
                await RecordFailureAsync(
                    supplierId, $"The ERP's field lists could not be read. {refusal.Message}", final: false, now, ct);
            }

            return;
        }
        catch (ErpNotConfiguredException stop)
        {
            logger.LogInformation("Supplier push to the ERP stopped: {Reason}", stop.Message);
            return;
        }

        foreach (var supplierId in due)
        {
            db.ChangeTracker.Clear();

            if (!await PushOneAsync(supplierId, fields, group, now, ct)) return;
        }
    }

    // One supplier's attempt, from the payload to the completed push. It answers whether the run may go on to the next
    // supplier: a refused credential, a switch turned off or a connection disabled stops the run.
    //
    // ANYTHING UNEXPECTED IS COUNTED AS A FAILED ATTEMPT, and the next supplier goes on. The tracker is cleared first, so
    // what this supplier left half-done does not ride along into the next save.
    private async Task<bool> PushOneAsync(Guid supplierId, Fields fields, string group, DateTimeOffset now, CancellationToken ct)
    {
        var supplier = await LoadAsync(supplierId, ct);
        if (supplier is null || !IsDue(supplier, now)) return true;

        var built = ErpSupplierPayload.Build(supplier, group, fields.Supplier, fields.Address, fields.Contact);
        if (built.Payload is not { } payload)
        {
            await RecordFailureAsync(supplierId, built.HeldReason!, final: true, now, ct);
            return true;
        }

        try
        {
            var erpName = supplier.ExternalId ?? await SupplierRecordAsync(supplier, payload, now, ct);
            if (erpName is null) return true;

            var made = await CreateTheRestAsync(erpName, payload, ct);
            await CompleteAsync(supplier, erpName, made, ct);
            return true;
        }
        catch (ErpRequestException refusal) when (refusal.PushKind == ErpPushFailureKind.Permission)
        {
            LogPermissionStop(refusal);
            return false;
        }
        catch (ErpRequestException refusal)
        {
            await RecordFailureAsync(supplierId, refusal.Message, refusal.PushKind == ErpPushFailureKind.Permanent, now, ct);
            return true;
        }
        catch (Exception stop) when (stop is ErpWritesOffException or ErpNotConfiguredException)
        {
            logger.LogInformation("Supplier push to the ERP stopped: {Reason}", stop.Message);
            return false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Pushing supplier {SupplierId} to the ERP failed.", supplierId);
            db.ChangeTracker.Clear();
            await RecordFailureAsync(supplierId, $"The push stopped part-way: {exception.Message}", final: false, now, ct);
            return true;
        }
    }

    // The Supplier record, for a supplier the ERP does not hold under a name the portal knows: found, or created and
    // linked at once. It answers the ERP's name, or null when the attempt has been recorded as failed.
    private async Task<string?> SupplierRecordAsync(
        Supplier supplier, ErpSupplierPayload payload, DateTimeOffset now, CancellationToken ct)
    {
        if (supplier.ErpPushStartedAt is not null || supplier.ErpPushLastError is not null)
        {
            var earlier = await FindEarlierCreateAsync(supplier, payload, now, ct);

            if (earlier.Ambiguity is not null)
            {
                await RecordFailureAsync(supplier.Id, earlier.Ambiguity, final: true, now, ct);
                return null;
            }

            if (earlier.Name is not null)
            {
                await LinkAsync(supplier, earlier.Name, FoundNote(earlier), now, ct);
                return earlier.Name;
            }
        }

        var before = Snapshot(supplier);
        supplier.BeginErpPush(now);
        await WriteAsync(supplier, before, action: null, reason: null, ct);

        string erpName;
        try
        {
            erpName = await registrar.CreateSupplierAsync(payload.Supplier(), ct);
        }
        catch (ErpRequestException unsure)
            when (unsure.PushKind is ErpPushFailureKind.OutcomeUnknown or ErpPushFailureKind.AlreadyExists)
        {
            return await AfterUnsureCreateAsync(supplier, payload, unsure, now, ct);
        }

        await LinkAsync(supplier, erpName, CreatedNote(erpName, payload.Notes), now, ct);
        return erpName;
    }

    // A create that got no answer may have made the record, and one the ERP says clashes means the ERP holds something
    // under that name or number already. Either way the portal looks before it does anything else. Found, it is linked.
    // Not found after no answer, the attempt is counted and the next one looks again before it creates, because the ERP
    // may still finish a request the portal stopped waiting for. Not found after a clash, a person must decide.
    private async Task<string?> AfterUnsureCreateAsync(
        Supplier supplier, ErpSupplierPayload payload, ErpRequestException unsure, DateTimeOffset now, CancellationToken ct)
    {
        var earlier = await FindEarlierCreateAsync(supplier, payload, now, ct);

        if (earlier.Name is not null)
        {
            await LinkAsync(supplier, earlier.Name, FoundNote(earlier), now, ct);
            return earlier.Name;
        }

        if (earlier.Ambiguity is not null)
        {
            await RecordFailureAsync(supplier.Id, $"{unsure.Message} {earlier.Ambiguity}", final: true, now, ct);
        }
        else if (unsure.PushKind == ErpPushFailureKind.AlreadyExists)
        {
            await RecordFailureAsync(
                supplier.Id,
                $"{unsure.Message} No ERP supplier the portal created, and none with this tax number, was found, so a "
                + "person must decide which ERP supplier this is.",
                final: true,
                now,
                ct);
        }
        else
        {
            await RecordFailureAsync(
                supplier.Id,
                $"{unsure.Message} The ERP did not have the supplier when the portal looked straight afterwards; the next "
                + "attempt looks again before it creates.",
                final: false,
                now,
                ct);
        }

        return null;
    }

    // An ERP supplier that an earlier attempt of this push may have created: by tax number first, then by this name among
    // the Suppliers the portal's API user created since the push was requested. Suppliers another portal supplier
    // already carries are left out, because the import matches each ERP supplier to one portal supplier only.
    private async Task<EarlierCreate> FindEarlierCreateAsync(
        Supplier supplier, ErpSupplierPayload payload, DateTimeOffset now, CancellationToken ct)
    {
        if (payload.TaxId is { } taxId)
        {
            var byTaxId = await UnclaimedAsync(await registrar.FindSuppliersByTaxIdAsync(taxId, ct), ct);
            if (byTaxId.Count > 0) return Decide(byTaxId, $"tax number {taxId}");
        }

        var since = new[] { supplier.ErpPushRequestedAt, supplier.ErpPushStartedAt, now }.Min()!.Value - LookBack;
        var byName = await UnclaimedAsync(
            await registrar.FindSuppliersCreatedByPortalAsync(payload.SupplierName, since, ct), ct);

        return byName.Count == 0
            ? new EarlierCreate(null, null, null)
            : Decide(byName, $"the name \"{payload.SupplierName}\", created by the portal");
    }

    private static EarlierCreate Decide(IReadOnlyList<string> names, string how) =>
        names.Count == 1
            ? new EarlierCreate(names[0], how, null)
            : new EarlierCreate(
                null,
                how,
                $"The ERP has {names.Count} suppliers with {how} that no portal supplier carries "
                + $"({string.Join(", ", names)}), so a person must decide which one this supplier is.");

    private async Task<IReadOnlyList<string>> UnclaimedAsync(IReadOnlyList<ErpSupplierMatch> matches, CancellationToken ct)
    {
        var names = matches.Select(m => m.Name).Distinct(StringComparer.Ordinal).ToList();
        if (names.Count == 0) return names;

        var claimed = await db.Suppliers
            .AsNoTracking()
            .Where(s => s.ExternalId != null && names.Contains(s.ExternalId))
            .Select(s => s.ExternalId!)
            .ToListAsync(ct);

        return [.. names.Where(name => !claimed.Contains(name, StringComparer.Ordinal))];
    }

    // Calls two to six, each only for what the ERP does not have yet. It answers what this attempt made, for the audit
    // row. A supplier whose representative has no deliverable email gets no website user, as ErpSupplierPayload says,
    // and so no portal user and no link from the contact.
    private async Task<IReadOnlyList<string>> CreateTheRestAsync(
        string erpName, ErpSupplierPayload payload, CancellationToken ct)
    {
        var made = new List<string>();

        var address = payload.Address(erpName);
        if (!(await registrar.ListLinkedAddressesAsync(erpName, ct)).Any(existing => SameAddress(existing, address)))
        {
            made.Add($"address {await registrar.CreateAddressAsync(address, ct)}");
        }

        var contacts = await registrar.ListLinkedContactsAsync(erpName, ct);
        var contact = payload.UserEmail is { } email
            ? contacts.FirstOrDefault(c => SameText(c.Email, email))
            : contacts.FirstOrDefault();

        var contactMadeNow = contact is null;
        if (contact is null)
        {
            contact = new ErpLinkedContact(await registrar.CreateContactAsync(payload.Contact(erpName), ct), null, null);
            made.Add($"contact {contact.Name}");
        }

        if (payload.UserEmail is not { } userEmail || payload.User() is not { } userBody) return made;

        var user = await registrar.FindUserAsync(userEmail, ct);
        var userCameFirst = user is not null;
        if (user is null)
        {
            user = await registrar.CreateUserAsync(userBody, ct);
            made.Add($"website user {user}");
        }

        await registrar.AddPortalUserAsync(erpName, user, ct);

        if (contactMadeNow ? !userCameFirst : !SameText(contact.User, user))
        {
            await registrar.SetContactUserAsync(contact.Name, user, ct);
            made.Add($"contact {contact.Name} linked to {user}");
        }

        return made;
    }

    // Saving the ERP's name as the supplier's ExternalId, in a save of its own, before any other call.
    private async Task LinkAsync(Supplier supplier, string erpName, string reason, DateTimeOffset now, CancellationToken ct)
    {
        var before = Snapshot(supplier);
        supplier.RecordErpSupplierCreated(erpName, now);
        await WriteAsync(supplier, before, SupplierAuditActions.ErpPushCreated, reason, ct);

        logger.LogInformation("Supplier {ReferenceCode} is ERP supplier {ErpName}.", supplier.ReferenceCode, erpName);
    }

    private async Task CompleteAsync(Supplier supplier, string erpName, IReadOnlyList<string> made, CancellationToken ct)
    {
        var before = Snapshot(supplier);
        supplier.CompleteErpPush();
        await WriteAsync(
            supplier,
            before,
            SupplierAuditActions.ErpPushCompleted,
            made.Count == 0
                ? $"ERP supplier {erpName} already had everything the push creates."
                : $"ERP supplier {erpName} is complete. This attempt made: {string.Join("; ", made)}.",
            ct);

        logger.LogInformation("Supplier {ReferenceCode} is complete in the ERP as {ErpName}.", supplier.ReferenceCode, erpName);
    }

    // A failed attempt, recorded on the supplier as it now stands in the database rather than on this run's copy, which
    // may be ahead of what was saved. A final failure, or the last attempt allowed, fails the push; anything else waits.
    // Recording must never hide the failure that led to it, so an error here is logged and the run goes on.
    private async Task RecordFailureAsync(Guid supplierId, string message, bool final, DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            db.ChangeTracker.Clear();

            var supplier = await LoadAsync(supplierId, ct);
            if (supplier?.ErpPushStatus is not (SupplierErpPushStatus.Requested or SupplierErpPushStatus.Linked)) return;

            var before = Snapshot(supplier);
            var failedAttempts = supplier.ErpPushAttempts + 1;

            if (final || failedAttempts >= MaxAttempts)
            {
                supplier.FailErpPush(final ? message : $"Stopped after {MaxAttempts} failed attempts. The last: {message}");
                await WriteAsync(supplier, before, SupplierAuditActions.ErpPushFailed, supplier.ErpPushLastError, ct);

                logger.LogWarning(
                    "The ERP push of supplier {ReferenceCode} has failed and waits for a person: {Reason}",
                    supplier.ReferenceCode, supplier.ErpPushLastError);
                return;
            }

            var next = now + WaitAfter(failedAttempts);
            supplier.RecordErpPushAttemptFailed(message, next);
            await WriteAsync(
                supplier,
                before,
                SupplierAuditActions.ErpPushAttemptFailed,
                $"{supplier.ErpPushLastError} Attempt {failedAttempts} of {MaxAttempts}; the next is due at {next:yyyy-MM-dd HH:mm} UTC.",
                ct);

            logger.LogWarning(
                "An ERP push attempt for supplier {ReferenceCode} failed and is tried again at {NextAttemptAt}: {Reason}",
                supplier.ReferenceCode, next, message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not record the failed ERP push of supplier {SupplierId}.", supplierId);
        }
    }

    // The targeted update the header describes: the push's columns and ExternalId as the domain left them on this run's
    // copy, written only while the row still holds the status and ExternalId the copy started from, with its audit row
    // in the same transaction. Zero rows means something moved the push meanwhile, and nothing is saved.
    private async Task WriteAsync(Supplier supplier, PushColumns before, string? action, string? reason, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var written = await db.Suppliers
            .Where(s => s.Id == supplier.Id && s.ErpPushStatus == before.Status && s.ExternalId == before.ExternalId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.ExternalId, supplier.ExternalId)
                .SetProperty(s => s.ErpPushStatus, supplier.ErpPushStatus)
                .SetProperty(s => s.ErpPushStartedAt, supplier.ErpPushStartedAt)
                .SetProperty(s => s.ErpPushAttempts, supplier.ErpPushAttempts)
                .SetProperty(s => s.ErpPushNextAttemptAt, supplier.ErpPushNextAttemptAt)
                .SetProperty(s => s.ErpPushLastError, supplier.ErpPushLastError),
                ct);

        if (written != 1)
        {
            throw new InvalidOperationException(
                $"The ERP push of supplier {supplier.ReferenceCode} changed while this run was working on it, so nothing "
                + "was saved.");
        }

        if (action is not null)
        {
            await audit.LogAsync(
                aggregateType: "Supplier",
                aggregateId: supplier.Id,
                action: action,
                actorLabel: SystemActor,
                fromState: before.Status.ToString(),
                toState: supplier.ErpPushStatus.ToString(),
                reason: reason,
                referenceCode: supplier.ReferenceCode,
                ct: ct);

            await db.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }

    private IQueryable<Supplier> Due(Guid? only, DateTimeOffset now) =>
        db.Suppliers.AsNoTracking().Where(s =>
            s.OnboardingState == SupplierOnboardingState.Approved
            && (s.ErpPushStatus == SupplierErpPushStatus.Requested || s.ErpPushStatus == SupplierErpPushStatus.Linked)
            && s.ErpPushNextAttemptAt <= now
            && (only == null || s.Id == only));

    private static bool IsDue(Supplier supplier, DateTimeOffset now) =>
        supplier.OnboardingState == SupplierOnboardingState.Approved
        && supplier.ErpPushStatus is (SupplierErpPushStatus.Requested or SupplierErpPushStatus.Linked)
        && supplier.ErpPushNextAttemptAt <= now;

    private Task<Supplier?> LoadAsync(Guid supplierId, CancellationToken ct) =>
        db.Suppliers
            .AsNoTracking()
            .Include(s => s.Representatives)
            .Include(s => s.Addresses)
            .AsSplitQuery()
            .SingleOrDefaultAsync(s => s.Id == supplierId, ct);

    private static PushColumns Snapshot(Supplier supplier) => new(supplier.ErpPushStatus, supplier.ExternalId);

    private static string CreatedNote(string erpName, IReadOnlyList<string> notes) =>
        notes.Count == 0 ? $"Created in the ERP as {erpName}." : $"Created in the ERP as {erpName}. {string.Join(" ", notes)}";

    private static string FoundNote(EarlierCreate earlier) =>
        $"Linked to ERP supplier {earlier.Name}, found by {earlier.How}, because an earlier attempt may have created it; "
        + "it was not created again.";

    private static bool SameAddress(ErpLinkedAddress existing, JsonObject address) =>
        SameText(existing.Line1, Text(address["address_line1"])) && SameText(existing.City, Text(address["city"]));

    private static bool SameText(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private void LogPermissionStop(ErpRequestException refusal) =>
        logger.LogError(
            "Supplier push to the ERP stopped: the ERP refused the portal's credential. {Refusal} No failure was recorded "
            + "on any supplier. Give the API user the rights the push needs, and the next run carries on.",
            refusal.Message);

    private async Task ReleaseQuietlyAsync()
    {
        try
        {
            await ErpImportLock.ReleaseAsync(db);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not release the ERP lock; closing the connection releases it.");
        }
    }
}
