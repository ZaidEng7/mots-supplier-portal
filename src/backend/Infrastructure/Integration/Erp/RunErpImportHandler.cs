// The supplier import from the ERP, which an administrator starts by hand and the hourly sync starts on its own:
// creating the suppliers the portal does not hold yet, with their accounts, updating the ones it does, and suspending
// or marking the ones the ERP no longer returns.
//
// SAVED SUPPLIER BY SUPPLIER, NOT ONCE FOR THE RUN. Eighty suppliers means eighty chances to hit something nobody
// anticipated, and a single transaction would throw away seventy-nine good rows because of the eightieth. A new
// supplier is created in a transaction of its own, with its account and its audit rows; an update has no transaction
// and is saved as it goes - see UpdateAsync.
//
// A FAILED SUPPLIER IS FORGOTTEN BEFORE THE NEXT ONE. The run shares one database context, and a supplier whose save
// failed stays in it, waiting to be saved again; without clearing it, every later supplier's save would retry the
// failed one and fail with it, and one value too long for its column would empty the rest of the run.
//
// THE PASSWORD IS CHECKED ONCE, BEFORE ANYTHING IS WRITTEN. The identity framework enforces twelve characters and
// refuses passwords found in public breaches, and discovering that on the first account leaves a supplier row
// with no account attached and seventy-nine to go. So a throwaway validation runs first and the whole run stops
// with one clear message instead.
//
// THE ACCOUNT IS CREATED ALREADY CONFIRMED, and that is not a shortcut. Sign-in refuses an unconfirmed address -
// LoginHandler turns it away even with the right password - so an imported account that was not confirmed could
// never be used at all, and the shared password would be useless. Nobody clicked a link because nobody was sent
// one; the ministry is asserting these addresses, not verifying them, and that is what this records.
//
// NO EMAIL IS SENT. That was asked for, and the reason to write it down is that the invitation path still exists
// and is the better answer for the real registry: eighty real companies receiving an unexpected email from the
// ministry is not a thing to do by accident, and the shared password this replaces it with is discussed in
// ErpImportOptions.
//
// A SUPPLIER ALREADY CARRYING THIS ERP'S IDENTIFIER IS UPDATED, NEVER DUPLICATED, and it keeps its account - no second
// user, no password reset. Pressing this button twice is a thing that will happen. The one change the import makes to
// that account is moving a placeholder login to the real address, once the ERP has one.
//
// IT DOES WHAT THE PREVIEW SAID, because both make every decision through the same rules, on the portal as it stood
// before the first write: LinkedSuppliersInPortal reads the ERP-linked suppliers, ErpSyncPlan decides who is held for
// a person, suspended or marked, ErpImportAdmission fills each gap, ErpRegistrationNumbers settles who carries which
// registration number, and ErpStandingDecision decides what the ERP's standing does to a supplier already here. This
// handler applies those decisions through the Supplier record and writes the audit rows. What it checks that the
// preview does not - whether an address already belongs to another account, whether a login can move, whether the
// automatic reinstatement agrees - depends on the accounts and documents at the moment it writes.
//
// EVERY SUPPLIER IS ADMITTED. Gaps are filled - a placeholder email, an empty currency, suspension for a supplier
// the ERP has disabled or not approved - by ErpImportAdmission, and every filled gap is written into the row's notes.
// Three refusals are left: an address that already belongs to another account, because two suppliers cannot share one
// login; a probable rename, which is held for a person to decide; and an ERP supplier the portal's own push created
// and has not linked yet, which is the push's to link - see ErpSyncPlan for both.
//
// REFUSALS AND FAILURES ARE COUNTED SEPARATELY. A refusal is a supplier the portal declined for a stated reason; a
// failure is the import going wrong. One number for both would hide a defect inside an expected result.
//
// IT READS THE WHOLE REGISTRY ON PURPOSE, AND IT IS NOT ROW-SCOPED. It matches the ERP against every supplier the
// portal holds; a view limited to one organisation would report every supplier it could not see as missing, and
// suspend them. It does read the caller's scope - but only to name the person who pressed the button on the audit
// trail. RowScopeGuardTests matches on that token, so it counts this handler as scoped and lists no exemption for it;
// that verdict is about attribution, not about which rows are read.
//
// THE RUN'S AUDIT ROW IS WRITTEN AND SAVED BEFORE THE FIRST SUPPLIER IS TOUCHED, so a run that fails part-way is still
// on the trail. A LogAsync with no SaveChangesAsync after it writes nothing, a mistake this product has made before.
//
// AND THE RUN'S END IS WRITTEN TOO: ErpImportCompleted when it finished, ErpImportFailed when it threw, with the actor
// ErpImportRun names and, in Changes, the trigger and the counts, or what went wrong. Each is saved with the outcome on
// the connection row, which keeps only the latest run for the screen; the trail keeps every one, so "what did the
// hourly sync do on Tuesday" has an answer. A run refused because the lock is taken never started, and writes no row
// at all.
//
// A CLOSING ROW THAT CANNOT BE RECORDED IS HANDLED BY WHICH ROW IT WAS. ErpImportCompleted is recorded inside the run's
// try, so a throw there closes the run as one that threw: ErpImportFailed is saved in its place, naming that exception,
// and the exception reaches the caller instead of the report, though every supplier the run wrote stays written. A
// throw while recording ErpImportFailed is logged and swallowed by RecordFailureAsync, so the exception that ended the
// run is still the one the caller sees, and the trail keeps the opening row with no closing one.

namespace MotsSupplierPortal.Infrastructure.Integration.Erp;

using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Integration;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Integration;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Registrations;
using MotsSupplierPortal.Infrastructure.Suppliers;

public sealed class RunErpImportHandler(
    IErpSupplierSource source,
    AppDbContext db,
    UserManager<AppUser> userManager,
    IOptions<ErpImportOptions> options,
    IAuditLogger audit,
    IScopeContext scope,
    ILogger<RunErpImportHandler> logger) : IRunErpImportHandler
{
    // WHO IS ACCOUNTABLE FOR A RUN IS RECORDED. A person pressing the button is named on every audit row the run
    // writes, including the suppliers it suspends; a scheduled run has nobody to name, so it is attributed to the
    // system rather than left blank. A suspension nobody can trace is the kind of change somebody asks about later.
    public async Task<ErpImportRunReport> HandleAsync(ErpImportTrigger trigger, CancellationToken ct)
    {
        var actor = new Actor(
            trigger == ErpImportTrigger.Manual ? scope.UserId : null,
            trigger == ErpImportTrigger.Scheduled ? "system" : null);

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            if (!await ErpImportLock.TryAcquireAsync(db, ct))
            {
                throw new ErpImportBusyException();
            }

            try
            {
                var (report, probableRenames) = await RunLockedAsync(actor, ct);
                await RecordAsync(
                    actor,
                    SupplierAuditActions.ErpImportCompleted,
                    OutcomeOf(report, probableRenames),
                    Summary(report, probableRenames),
                    new
                    {
                        trigger = trigger.ToString(),
                        erpSuppliers = report.ErpSupplierCount,
                        created = report.Created,
                        updated = report.Updated,
                        suspended = report.Suspended,
                        refused = report.Refused,
                        failed = report.Failed,
                    },
                    CancellationToken.None);
                return report;
            }
            catch (Exception exception)
            {
                await RecordFailureAsync(actor, trigger, exception);
                throw;
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

    private sealed record Actor(Guid? UserId, string? Label);

    // The run itself, under the lock.
    //
    // EVERYTHING THE PLAN DECIDES IS DECIDED BEFORE THE FIRST SUPPLIER IS WRITTEN, from the portal as it stood before
    // this run, read by LinkedSuppliersInPortal - the reader the preview uses too, so both build the same plan.
    // Deciding after creating this run's new suppliers would let every new one raise the limit the missing ones are
    // judged against. Probable renames are held for a person rather than re-linked; why is in ErpSyncPlan.
    private async Task<(ErpImportRunReport Report, int ProbableRenames)> RunLockedAsync(Actor actor, CancellationToken ct)
    {
        await audit.LogAsync(
            aggregateType: "Supplier",
            aggregateId: Guid.Empty,
            action: SupplierAuditActions.ErpImportRun,
            actorUserId: actor.UserId,
            actorLabel: actor.Label,
            ct: ct);

        await db.SaveChangesAsync(ct);

        var password = options.Value.InitialPassword;
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ErpImportNotConfiguredException(
                "No initial password is configured: set ErpImport:InitialPassword.");
        }

        await EnsurePasswordIsAcceptableAsync(password);

        var registrationNumbersInPortal = await RegistrationNumbersInPortal.ReadAsync(db, ct);
        var erpSuppliers = await source.ListSuppliersAsync(ct);
        var registrationNumbers = ErpRegistrationNumbers.Decide(erpSuppliers, registrationNumbersInPortal);

        var plan = ErpSyncPlan.Build(erpSuppliers, await LinkedSuppliersInPortal.ReadAsync(db, ct));

        var renamedFrom = plan.ProbableRenames.ToDictionary(r => r.NewExternalId, r => r, StringComparer.Ordinal);
        var rows = new List<ErpImportResultRow>();

        foreach (var erpSupplier in erpSuppliers)
        {
            if (plan.IsHeldForPush(erpSupplier.ExternalId))
            {
                rows.Add(new ErpImportResultRow(
                    erpSupplier.ExternalId,
                    ErpImportAdmission.Admit(erpSupplier).Name,
                    ErpImportOutcome.Refused,
                    null,
                    [ErpImportPreviewBuilder.HeldForPushNote]));
                continue;
            }

            if (renamedFrom.TryGetValue(erpSupplier.ExternalId, out var rename))
            {
                rows.Add(new ErpImportResultRow(
                    erpSupplier.ExternalId,
                    ErpImportAdmission.Admit(erpSupplier).Name,
                    ErpImportOutcome.Refused,
                    rename.ReferenceCode,
                    [ErpImportPreviewBuilder.ProbableRenameNote(rename)]));
                continue;
            }

            rows.Add(await ImportOneAsync(
                erpSupplier,
                registrationNumbers[erpSupplier.ExternalId],
                password,
                actor,
                plan.HoldsTurnedAwayFor(erpSupplier.ExternalId),
                ct));
        }

        rows.AddRange(await SuspendPlannedAsync(plan.ToSuspend, actor, ct));
        await MarkRemovedAsync(plan.ToMarkRemoved, actor, ct);

        var report = new ErpImportRunReport(
            ErpSupplierCount: erpSuppliers.Count,
            Created: rows.Count(r => r.Outcome == ErpImportOutcome.Created),
            Updated: rows.Count(r => r.Outcome == ErpImportOutcome.Updated),
            Refused: rows.Count(r => r.Outcome == ErpImportOutcome.Refused),
            Failed: rows.Count(r => r.Outcome == ErpImportOutcome.Failed),
            Rows: rows,
            Suspended: rows.Count(r => r.Outcome == ErpImportOutcome.Suspended),
            SuspensionsHeldBack: plan.SuspensionsHeldBack);

        logger.LogInformation("ERP import finished: {Summary}", Summary(report, plan.ProbableRenames.Count));

        return (report, plan.ProbableRenames.Count);
    }

    // Suspending what the plan decided, re-checked under the lock: a supplier a person reinstated, or one already
    // suspended as removed, between the plan and this moment is left alone. One only marked as removed while it was
    // out of service, and since back in service, is suspended like any other - see ErpSyncPlan.
    private async Task<List<ErpImportResultRow>> SuspendPlannedAsync(
        IReadOnlyList<PortalLinkedSupplier> planned, Actor actor, CancellationToken ct)
    {
        if (planned.Count == 0) return [];

        var ids = planned.Select(p => p.ExternalId).ToList();

        var suppliers = await db.Suppliers
            .Where(s => s.ExternalId != null
                && ids.Contains(s.ExternalId)
                && s.LifecycleState == SupplierLifecycleState.Active
                && s.SyncStatus != SupplierSyncStatus.RemovedFromErp)
            .OrderBy(s => s.ReferenceCode)
            .ToListAsync(ct);

        const string Reason = "No longer in the ERP.";
        var rows = new List<ErpImportResultRow>();

        foreach (var supplier in suppliers)
        {
            supplier.SuspendAsRemovedFromErp();

            await audit.LogAsync(
                aggregateType: "Supplier",
                aggregateId: supplier.Id,
                action: SupplierAuditActions.SuspendedMissingFromErp,
                actorUserId: actor.UserId,
                actorLabel: actor.Label,
                fromState: nameof(SupplierLifecycleState.Active),
                toState: nameof(SupplierLifecycleState.Suspended),
                reason: Reason,
                referenceCode: supplier.ReferenceCode,
                ct: ct);

            rows.Add(new ErpImportResultRow(
                supplier.ExternalId!,
                supplier.DisplayNameEn,
                ErpImportOutcome.Suspended,
                supplier.ReferenceCode,
                ["No longer in the ERP; suspended - kept in the registry, but cannot be invited to tenders. A person "
                 + "who reinstates it will not be overruled by the next run."]));
        }

        await db.SaveChangesAsync(ct);

        return rows;
    }

    // Suppliers already out of service that have now also left the ERP are marked, not suspended - they are suspended
    // or deactivated already. The mark is what stops them standing in for a new company that shares their tax number
    // on some later run. Re-checked under the lock, like the suspensions.
    private async Task MarkRemovedAsync(IReadOnlyList<PortalLinkedSupplier> planned, Actor actor, CancellationToken ct)
    {
        if (planned.Count == 0) return;

        var ids = planned.Select(p => p.ExternalId).ToList();

        var suppliers = await db.Suppliers
            .Where(s => s.ExternalId != null
                && ids.Contains(s.ExternalId)
                && s.LifecycleState != SupplierLifecycleState.Active
                && s.SyncStatus != SupplierSyncStatus.RemovedFromErp
                && s.SyncStatus != SupplierSyncStatus.MarkedRemovedFromErp)
            .ToListAsync(ct);

        foreach (var supplier in suppliers)
        {
            supplier.MarkRemovedFromErp();

            await audit.LogAsync(
                aggregateType: "Supplier",
                aggregateId: supplier.Id,
                action: SupplierAuditActions.MarkedRemovedFromErp,
                actorUserId: actor.UserId,
                actorLabel: actor.Label,
                reason: "No longer in the ERP; already out of service here, so only marked.",
                referenceCode: supplier.ReferenceCode,
                ct: ct);
        }

        await db.SaveChangesAsync(ct);
    }

    private static string Summary(ErpImportRunReport report, int probableRenames)
    {
        var summary =
            $"{report.ErpSupplierCount} in the ERP: {report.Created} created, {report.Updated} updated, "
            + $"{report.Suspended} suspended, {report.Refused} refused, {report.Failed} failed.";

        if (probableRenames > 0)
        {
            summary += $" {probableRenames} possible rename(s) held for a person - see the import report.";
        }

        return report.SuspensionsHeldBack is null ? summary : $"{summary} {report.SuspensionsHeldBack}";
    }

    // A run that finished but left something only a person can resolve is not a success, and not a failure either.
    private static IntegrationSyncOutcome OutcomeOf(ErpImportRunReport report, int probableRenames) =>
        report.Failed > 0 || report.SuspensionsHeldBack is not null || probableRenames > 0
            ? IntegrationSyncOutcome.NeedsAttention
            : IntegrationSyncOutcome.Succeeded;

    // The run's outcome, recorded in one save in two places: on the ERP connection, where the screen reads the latest
    // run, and as the row that closes the run on the audit trail, under the actor ErpImportRun named, with the outcome
    // as its state, the summary as its reason and the details as JSON. A deployment with no connection row still gets
    // the audit row.
    private async Task RecordAsync(
        Actor actor,
        string action,
        IntegrationSyncOutcome outcome,
        string summary,
        object details,
        CancellationToken ct)
    {
        var connection = await db.IntegrationConnections
            .FirstOrDefaultAsync(c => c.Key == IntegrationConnection.ErpKey, ct);
        connection?.RecordSync(outcome, summary);

        await audit.LogAsync(
            aggregateType: "Supplier",
            aggregateId: Guid.Empty,
            action: action,
            actorUserId: actor.UserId,
            actorLabel: actor.Label,
            toState: outcome.ToString(),
            reason: summary,
            changes: JsonSerializer.Serialize(details),
            ct: ct);

        await db.SaveChangesAsync(ct);
    }

    // A failed or interrupted run is recorded as such, and recording it must never hide why it failed. So the tracker is
    // cleared first - whatever half-finished change caused the failure must not ride along into this save, and the
    // ErpImportFailed row is added after the clear, or the clear would take it out again - and any error while
    // recording is logged and swallowed, leaving the original exception to reach whoever ran the import. The failure is
    // "Interrupted" for a cancelled run, and otherwise the exception's type, which names the setting or the system at
    // fault; the run's counts are not known, because a run that throws returns no report, and the suppliers it had
    // already written carry their own rows.
    private async Task RecordFailureAsync(Actor actor, ErpImportTrigger trigger, Exception exception)
    {
        var interrupted = exception is OperationCanceledException;

        try
        {
            db.ChangeTracker.Clear();
            await RecordAsync(
                actor,
                SupplierAuditActions.ErpImportFailed,
                IntegrationSyncOutcome.Failed,
                interrupted ? "The import was interrupted before it finished." : $"The import failed: {exception.Message}",
                new
                {
                    trigger = trigger.ToString(),
                    failure = interrupted ? "Interrupted" : exception.GetType().Name,
                    message = exception.Message,
                },
                CancellationToken.None);
        }
        catch (Exception recording)
        {
            logger.LogError(recording, "Could not record the failed ERP import.");
        }
    }

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

    private async Task<ErpImportResultRow> ImportOneAsync(
        ErpSupplier erpSupplier,
        ErpRegistrationNumberDecision registrationNumber,
        string password,
        Actor actor,
        bool holdTurnedAway,
        CancellationToken ct)
    {
        var admitted = ErpImportAdmission.Admit(erpSupplier);
        if (registrationNumber.Note is not null)
        {
            admitted = admitted with { Notes = [.. admitted.Notes, registrationNumber.Note] };
        }

        var details = new ErpSupplierDetails(
            DisplayNameAr: admitted.ArabicName,
            RegistrationNumber: registrationNumber.Number,
            RegistrationType: admitted.RegistrationType,
            SupplierGroup: admitted.SupplierGroup,
            Description: admitted.Description);

        try
        {
            var existing = await db.Suppliers
                .Include(s => s.Representatives)
                .Include(s => s.Addresses)
                .AsSplitQuery()
                .FirstOrDefaultAsync(s => s.ExternalId == erpSupplier.ExternalId, ct);

            return existing is null
                ? await CreateAsync(erpSupplier, admitted, details, password, actor, ct)
                : await UpdateAsync(existing, erpSupplier, admitted, details, actor, holdTurnedAway, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Importing {Supplier} failed.", erpSupplier.ExternalId);
            db.ChangeTracker.Clear();

            return new ErpImportResultRow(
                erpSupplier.ExternalId, admitted.Name, ErpImportOutcome.Failed, null, [exception.Message]);
        }
    }

    // Creating a supplier the portal has never held, with its account.
    //
    // A SUPPLIER THE IMPORT CREATES IS ON THE AUDIT TRAIL FROM ITS FIRST MOMENT: a row saying it came from the ERP and
    // whose run brought it, and, if it arrives suspended, a row saying why, as every other suspension in the product
    // names its cause. The ERP's turn-away is written under the same action ApplyErpStandingAsync uses for a supplier
    // already here, so a search for suppliers the ERP disabled finds the ones that arrived that way too.
    //
    // THE ROWS ARE WRITTEN INSIDE THE SUPPLIER'S TRANSACTION, after its account exists, so the supplier, its account
    // and its rows are committed together or not at all: a row that cannot be written takes the supplier and its
    // account back out, and a supplier whose account could not be made gets no rows. Written after the commit, a
    // failed write would leave a supplier in the registry with nothing on its trail.
    private async Task<ErpImportResultRow> CreateAsync(
        ErpSupplier erpSupplier,
        AdmittedSupplier admitted,
        ErpSupplierDetails details,
        string password,
        Actor actor,
        CancellationToken ct)
    {
        var taken = await userManager.FindByEmailAsync(admitted.Email);
        if (taken is not null)
        {
            return new ErpImportResultRow(
                erpSupplier.ExternalId,
                admitted.Name,
                ErpImportOutcome.Refused,
                null,
                [$"The address {admitted.Email} already belongs to another account in the portal.", .. admitted.Notes]);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var referenceCode = await ReferenceCodeGenerator.NextSupplierCodeAsync(db, ct);

        var supplier = Supplier.ImportFromErp(
            referenceCode: referenceCode,
            externalId: erpSupplier.ExternalId,
            displayNameEn: admitted.Name,
            taxId: erpSupplier.TaxId,
            legalType: LegalTypeOf(erpSupplier.LegalType),
            currencyCode: admitted.Currency,
            representativeName: admitted.RepresentativeName,
            representativeEmail: admitted.Email,
            representativePhone: erpSupplier.Phone,
            standing: admitted.Standing,
            details: details);

        var address = ErpImportAdmission.AddressOutcome(admitted, isNew: true, addressesInPortal: 0, blockedByState: null);
        if (address.Write) AddAddress(supplier, admitted.Address!.Address);

        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync(ct);

        var user = new AppUser
        {
            Id = Guid.CreateVersion7(),
            UserName = admitted.Email,
            Email = admitted.Email,
            FullName = admitted.RepresentativeName,
            SupplierId = supplier.Id,
            EmailConfirmed = true,
        };

        var created = await userManager.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();

            return new ErpImportResultRow(
                erpSupplier.ExternalId,
                admitted.Name,
                ErpImportOutcome.Failed,
                null,
                [.. created.Errors.Select(error => error.Description)]);
        }

        await userManager.AddToRoleAsync(user, Roles.SupplierAdmin);

        supplier.Representatives[0].UserId = user.Id;

        await audit.LogAsync(
            aggregateType: "Supplier",
            aggregateId: supplier.Id,
            action: SupplierAuditActions.ImportedFromErp,
            actorUserId: actor.UserId,
            actorLabel: actor.Label,
            toState: nameof(SupplierOnboardingState.Approved),
            reason: $"Imported from the ERP, where it is {erpSupplier.ExternalId}, and approved without portal review.",
            referenceCode: referenceCode,
            ct: ct);

        if (admitted.Standing != ErpStanding.Usable)
        {
            await audit.LogAsync(
                aggregateType: "Supplier",
                aggregateId: supplier.Id,
                action: admitted.Standing == ErpStanding.Disabled
                    ? SupplierAuditActions.SuspendedDisabledInErp
                    : SupplierAuditActions.SuspendedNotApprovedInErp,
                actorUserId: actor.UserId,
                actorLabel: actor.Label,
                toState: nameof(SupplierLifecycleState.Suspended),
                reason: $"{admitted.TurnedAway}; it arrived suspended.",
                referenceCode: referenceCode,
                ct: ct);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new ErpImportResultRow(
            erpSupplier.ExternalId,
            admitted.Name,
            ErpImportOutcome.Created,
            referenceCode,
            [$"Approved without portal review, imported from the ERP. Account created for {admitted.Email}.",
             .. admitted.Notes,
             .. (admitted.ArrivalNote is null ? Array.Empty<string>() : [admitted.ArrivalNote]),
             address.Note]);
    }

    // Updating a supplier the portal already holds, in these steps and in this order; the rules of each are on its
    // own method.
    //
    //   ApplySnapshotAndAddress         copies what the ERP sends, and adds its address to a supplier that has none
    //   ApplyErpStandingAsync           suspends, marks or releases it as ErpStandingDecision decides
    //   MoveLoginOffPlaceholderAsync    moves a placeholder login to the real address the ERP now has
    //   MarkSynced                      records that the ERP has it, unless the decision keeps a mark as gone
    //   ReinstateOnceMarkClearsAsync    asks the automatic reinstatement again when the decision says a mark cleared
    //
    // and then one save, for whatever the steps left unsaved.
    //
    // IT HAS NO TRANSACTION, UNLIKE CreateAsync, SO IT IS NOT SAVED IN ONE PIECE. A login that moves saves the shared
    // context part-way through (see MoveLoginOffPlaceholderAsync), so a later step that throws leaves the earlier ones
    // saved while the row reports the supplier as failed.
    //
    // WHILE THE PLAN HOLDS A MASS TURN-AWAY BACK, A TURNED-AWAY SUPPLIER'S MEMORY IS NOT TOUCHED: its standing is not
    // recorded, and one marked as gone keeps the mark, because the decision skips MarkSynced - ErpStandingDecision says
    // why. Otherwise MarkSynced runs on every update, told whether a step changed the supplier; its comment says when
    // that moves LastSyncedAt.
    private async Task<ErpImportResultRow> UpdateAsync(
        Supplier existing,
        ErpSupplier erpSupplier,
        AdmittedSupplier admitted,
        ErpSupplierDetails details,
        Actor actor,
        bool holdTurnedAway,
        CancellationToken ct)
    {
        var notes = new List<string>
        {
            "Updated from the ERP. Documents, bank details and anything else the portal holds were left alone.",
        };

        var realEmail = admitted.EmailIsPlaceholder ? null : admitted.Email;

        var changed = ApplySnapshotAndAddress(existing, erpSupplier, admitted, details, realEmail, notes);
        var decision = await ApplyErpStandingAsync(existing, erpSupplier, admitted, holdTurnedAway, actor, ct);
        notes.AddRange(decision.Notes);

        if (realEmail is not null)
        {
            var moved = await MoveLoginOffPlaceholderAsync(existing, realEmail, ct);
            if (moved is not null) notes.Add(moved);
        }

        if (decision.MarksSynced)
        {
            existing.MarkSynced(
                erpSupplier.ExternalId,
                changed
                || decision.Change is not (ErpDisabledChange.None or ErpDisabledChange.ReleaseWaitsForDocuments));
        }

        if (decision.AsksReinstatement)
        {
            await ReinstateOnceMarkClearsAsync(existing, actor, notes, ct);
        }

        await db.SaveChangesAsync(ct);

        notes.AddRange(admitted.Notes);

        return new ErpImportResultRow(
            erpSupplier.ExternalId,
            admitted.Name,
            decision.Change == ErpDisabledChange.Suspended ? ErpImportOutcome.Suspended : ErpImportOutcome.Updated,
            existing.ReferenceCode,
            notes);
    }

    // Copying what the ERP sends onto the supplier, and adding the ERP's address if the supplier has none, with the
    // note that says what became of the address. It returns whether either changed the supplier, for MarkSynced.
    //
    // A PLACEHOLDER EMAIL NEVER OVERWRITES A REAL ONE, so realEmail is null when the ERP still has no email: the one on
    // file stays, and it may be a real address the supplier gave the portal since.
    //
    // THE ADDRESS IS ADDED ONLY TO A SUPPLIER THAT HAS NONE, and whose details may be edited. Once there is an address
    // the supplier or the ministry may have corrected it - placed the pin, fixed the governorate the ERP's city field
    // got wrong - and the ERP's version is the older truth; a supplier under review cannot have its contact details
    // changed underneath the reviewer. Every other field the ERP sends is refreshed, and nothing it lacks is blanked;
    // ApplyErpSnapshot has those rules.
    private bool ApplySnapshotAndAddress(
        Supplier existing,
        ErpSupplier erpSupplier,
        AdmittedSupplier admitted,
        ErpSupplierDetails details,
        string? realEmail,
        List<string> notes)
    {
        var changed = existing.ApplyErpSnapshot(
            displayNameEn: admitted.Name,
            taxId: erpSupplier.TaxId,
            legalType: LegalTypeOf(erpSupplier.LegalType),
            currencyCode: admitted.Currency,
            representativeEmail: realEmail,
            representativePhone: erpSupplier.Phone,
            details: details,
            representativeName: admitted.ContactPerson);

        var address = ErpImportAdmission.AddressOutcome(
            admitted,
            isNew: false,
            existing.Addresses.Count,
            Supplier.AllowsContactEdits(existing.OnboardingState) ? null : $"in state '{existing.OnboardingState}'");
        if (address.Write) AddAddress(existing, admitted.Address!.Address);
        notes.Add(address.Note);

        return changed || address.Write;
    }

    // Deciding what the ERP's standing does to the supplier, recording it, and writing the audit row for what that
    // changed. It returns the decision, whose notes the row carries.
    //
    // ErpStandingDecision decides, from the same facts the preview gives it, so the run does what the preview said it
    // would; its comment and RecordErpStanding's have the rules. The one fact this reads from the database is whether
    // an award-critical document that expired still has no approved renewal, asked only where it can matter: an ERP
    // approval for a supplier the sync suspended while it waited for that. Supplier.RecordErpStanding then makes the
    // change, unless the plan held it back, and makes exactly the decision's Change, because both come from
    // ErpDisabledChangeFor on the same memory. Each change is written under its action in SupplierAuditActions.
    //
    // A SUPPLIER THE ERP TURNS AWAY - disables, or has not approved - IS RECORDED THE WAY AN ABSENCE IS: suspended once
    // if it is active, only marked if it is out of service already, and a person's reinstatement after the one
    // suspension stands. The ERP's approval releases only a supplier the sync suspended while it waited for that and
    // that nobody has touched since, and not while an award-critical document that expired meanwhile has no approved
    // renewal.
    private async Task<ErpStandingDecision> ApplyErpStandingAsync(
        Supplier existing,
        ErpSupplier erpSupplier,
        AdmittedSupplier admitted,
        bool holdTurnedAway,
        Actor actor,
        CancellationToken ct)
    {
        var documentsAllowRelease = !(admitted.Standing == ErpStanding.Usable
            && existing.ErpDisabledState == SupplierErpDisabledState.SuspendedAsPending
            && await AwardCriticalRenewal.AwaitsRenewalAsync(db, existing.Id, ct));

        var decision = ErpStandingDecision.Decide(
            memory: existing.ErpDisabledState,
            isActive: existing.LifecycleState == SupplierLifecycleState.Active,
            standing: admitted.Standing,
            turnedAway: admitted.TurnedAway,
            documentsAllowRelease: documentsAllowRelease,
            planHoldsTurnedAway: holdTurnedAway,
            markedRemovedFromErp: existing.SyncStatus == SupplierSyncStatus.MarkedRemovedFromErp);

        if (!decision.HeldByLimit)
        {
            existing.RecordErpStanding(admitted.Standing, documentsAllowRelease);
        }

        switch (decision.Change)
        {
            case ErpDisabledChange.Suspended:
                await audit.LogAsync(
                    aggregateType: "Supplier",
                    aggregateId: existing.Id,
                    action: erpSupplier.Disabled
                        ? SupplierAuditActions.SuspendedDisabledInErp
                        : SupplierAuditActions.SuspendedNotApprovedInErp,
                    actorUserId: actor.UserId,
                    actorLabel: actor.Label,
                    fromState: nameof(SupplierLifecycleState.Active),
                    toState: nameof(SupplierLifecycleState.Suspended),
                    reason: $"{admitted.TurnedAway}.",
                    referenceCode: existing.ReferenceCode,
                    ct: ct);
                break;

            case ErpDisabledChange.Marked:
                await audit.LogAsync(
                    aggregateType: "Supplier",
                    aggregateId: existing.Id,
                    action: erpSupplier.Disabled
                        ? SupplierAuditActions.MarkedDisabledInErp
                        : SupplierAuditActions.MarkedNotApprovedInErp,
                    actorUserId: actor.UserId,
                    actorLabel: actor.Label,
                    reason: $"{admitted.TurnedAway}; already out of service here, so only marked.",
                    referenceCode: existing.ReferenceCode,
                    ct: ct);
                break;

            case ErpDisabledChange.ReleaseWithdrawn:
                await audit.LogAsync(
                    aggregateType: "Supplier",
                    aggregateId: existing.Id,
                    action: SupplierAuditActions.ErpReleaseWithdrawn,
                    actorUserId: actor.UserId,
                    actorLabel: actor.Label,
                    reason: $"{admitted.TurnedAway} while it waited for approval; "
                            + "its approval will no longer bring it back.",
                    referenceCode: existing.ReferenceCode,
                    ct: ct);
                break;

            case ErpDisabledChange.Released:
                await audit.LogAsync(
                    aggregateType: "Supplier",
                    aggregateId: existing.Id,
                    action: SupplierAuditActions.ReactivatedApprovedInErp,
                    actorUserId: actor.UserId,
                    actorLabel: actor.Label,
                    fromState: nameof(SupplierLifecycleState.Suspended),
                    toState: nameof(SupplierLifecycleState.Active),
                    reason: "Approved in the ERP; it had been suspended here only while it waited for that.",
                    referenceCode: existing.ReferenceCode,
                    ct: ct);
                break;
        }

        return decision;
    }

    // The new address is added to the context explicitly, as ManageAddressHandler does. Its identifier is set by the
    // domain, so on a supplier that is already tracked the change tracker takes it for an existing row and issues an
    // update that touches nothing - the save then fails as a concurrency conflict, and the supplier with it.
    private void AddAddress(Supplier supplier, MappedErpAddress? address)
    {
        if (address is null) return;

        db.Addresses.Add(supplier.AddAddress(
            AddressKind.Billing,
            address.Line1,
            address.Line2,
            address.City,
            address.RegionCode,
            address.Country,
            postalCode: null,
            latitude: null,
            longitude: null));
    }

    // Moving the supplier's login off its placeholder once the ERP has a real address, and the note that says what
    // happened; null when there is nothing to move.
    //
    // A REAL ADDRESS REPLACES A PLACEHOLDER ON THE LOGIN TOO, not only on the contact record. The account was made on
    // the placeholder, and sign-in finds an account by its address, so a contact updated while the login stayed on the
    // .invalid address would leave the supplier's actual person unable to sign in with the address everyone now has
    // on file. Only a placeholder login is moved: an account somebody has already changed to a real address is theirs.
    //
    // THE ADDRESS, THE USER NAME AND THE CONFIRMATION CHANGE TOGETHER, in one UserManager call that validates them
    // before it saves anything, so a refusal leaves the login as it was. That call is UpdateSecurityStampAsync, which
    // also gives the account the new security stamp any change of address or user name gets. One call per field could
    // save the new address and then refuse the user name, leaving an account whose address changed and is no longer
    // confirmed, which sign-in refuses. A refused change is also taken back off the tracked account, or the update's
    // own save would write it anyway. The note then says the login stayed on the placeholder, and the identity
    // framework's reason.
    //
    // USERMANAGER SAVES THE SHARED DATABASE CONTEXT. The identity store works on this run's context, so when the login
    // moves, everything the update has changed so far - the snapshot, the address, the standing and its audit row - is
    // saved at that point, not at the end of UpdateAsync.
    private async Task<string?> MoveLoginOffPlaceholderAsync(Supplier supplier, string realEmail, CancellationToken ct)
    {
        var userId = supplier.Representatives.FirstOrDefault(r => r.IsPrimary)?.UserId
            ?? supplier.Representatives.FirstOrDefault()?.UserId;
        if (userId is null) return null;

        var user = await userManager.FindByIdAsync(userId.Value.ToString());
        if (user is null || !ErpImportAdmission.IsPlaceholder(user.Email)) return null;

        if (await userManager.FindByEmailAsync(realEmail) is not null)
        {
            return $"The ERP now has {realEmail}, but another account already uses it; the login stays on the placeholder.";
        }

        var previous = user.Email;

        user.Email = realEmail;
        user.UserName = realEmail;
        user.EmailConfirmed = true;

        var result = await userManager.UpdateSecurityStampAsync(user);
        if (!result.Succeeded)
        {
            await db.Entry(user).ReloadAsync(ct);

            return $"The ERP now has {realEmail}, but the portal refused it for the login: "
                   + string.Join(" ", result.Errors.Select(error => error.Description))
                   + " The login stays on the placeholder.";
        }

        return $"The login moved from placeholder {previous} to {realEmail}.";
    }

    // Asking the automatic reinstatement again once a mark has cleared: the ERP returned a supplier marked as gone, or
    // lets a supplier marked as turned away be used again.
    //
    // THE MARK MAY HAVE HELD A REINSTATEMENT BACK. A supplier the expiry rule suspended may have had its renewed
    // document approved while the mark stood, and once the mark ends nothing else would look again;
    // AutomaticReinstatement explains the rest, and refuses while a mark still stands. The sync's own suspensions are
    // never lifted here: AutomaticReinstatement lifts only the expiry rule's, and reinstating somebody the ERP turned
    // away is a person's decision. The one the ERP's approval releases is released by ApplyErpStandingAsync.
    private async Task ReinstateOnceMarkClearsAsync(
        Supplier existing, Actor actor, List<string> notes, CancellationToken ct)
    {
        if (await AutomaticReinstatement.TryAsync(
                db,
                audit,
                existing,
                "Automatic reinstatement (BRULE-023/D-67): the award-critical document that expired was replaced and "
                + "approved while the ERP was not offering this supplier, and the ERP offers it again.",
                actor.UserId,
                actor.Label,
                $"{NotificationTypes.SupplierReinstated}:{existing.Id}:erp:{existing.LastSyncedAt?.UtcTicks}",
                ct))
        {
            notes.Add("Offered by the ERP again, and the expired document that suspended it has since been replaced "
                      + "and approved; reinstated automatically.");
        }
    }

    // A throwaway account is never saved: CreateAsync is not called, only the validators are, so nothing reaches
    // the database. It is the only way to ask the identity framework "would you accept this password" without
    // duplicating its rules here - and duplicating them is how the copy drifts from the real one.
    private async Task EnsurePasswordIsAcceptableAsync(string password)
    {
        var probe = new AppUser
        {
            Id = Guid.CreateVersion7(),
            UserName = "erp-import-probe",
            Email = "probe@invalid",
            FullName = "ERP import probe",
        };

        foreach (var validator in userManager.PasswordValidators)
        {
            var result = await validator.ValidateAsync(userManager, probe, password);
            if (result.Succeeded) continue;

            throw new ErpImportNotConfiguredException(
                "The configured import password is refused: "
                + string.Join(" ", result.Errors.Select(error => error.Description)));
        }
    }

    // The ERP's supplier_type in the portal's terms. The ERP offers Company, Individual and Partnership, spelt as the
    // portal spells them, and the push sends them that way. Each comes back as itself: a Partnership read as a Company
    // would rewrite, on every hourly run, a partnership the portal registered and pushed. Anything else, or nothing, is
    // a Company, the ERP's own default.
    private static SupplierLegalType LegalTypeOf(string? erpType) => erpType switch
    {
        "Individual" => SupplierLegalType.Individual,
        "Partnership" => SupplierLegalType.Partnership,
        _ => SupplierLegalType.Company,
    };
}

public sealed class ErpImportNotConfiguredException(string message) : Exception(message);
