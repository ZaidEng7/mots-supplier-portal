using System.Globalization;
using Microsoft.Extensions.Configuration;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Infrastructure.Configuration;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>
/// FEAT-05.5 / BRULE-025 / FR-NOT-006: moves Approved -> ExpiringSoon (within a configurable
/// window) and ExpiringSoon -> Expired at expiry, and chases renewal on an escalating cadence.
///
/// <para><b>What changed and why.</b> De-duplication was previously an accident of the state
/// machine: <c>MarkExpiringSoon</c> throws once a document is already ExpiringSoon, so the job could
/// only ever notify once. It behaved correctly and for the wrong reason, and the same accident made
/// BRULE-025 impossible - an escalating cadence needs to notify more than once, which the guard that
/// was providing the de-duplication forbade. Reminders are now recorded in
/// <see cref="DocumentExpiryReminder"/>, so escalation and de-duplication stop being in tension.</para>
///
/// <para><b>Assume this runs more than once a day.</b> Retries, host restarts, manual triggers and
/// schedule changes all re-run it. Nothing here is keyed on the run; everything is keyed on what has
/// already been communicated about a given document version.</para>
/// </summary>
public sealed class DocumentExpiryJob(
    AppDbContext db,
    IAuditLogger auditLogger,
    IBackgroundJobClient backgroundJobs,
    ISystemSettingReader settings)
{
    /// <summary>
    /// FR-DOC-006 calls this window configurable; it was a private static readonly const, which is
    /// the opposite. Changing it required a redeploy, and the comment beside it claimed
    /// "configurable" - an artifact asserting something untrue, the pattern this codebase keeps
    /// producing.
    ///
    /// Default stays 30 days so behaviour is unchanged where nothing is configured.
    ///
    /// <para><b>This window does NOT govern the reminder ladder.</b> It decides when a document
    /// enters ExpiringSoon (BRULE-021 / FR-DOC-006); the ladder decides when the supplier is told
    /// (BRULE-025), and it is bounded by its own widest threshold. The two numbers coincide only at
    /// the shared default of 30, which is exactly why the coupling is easy to assume and worth
    /// stating. Change this window and read <see cref="ReminderThresholdDaysAsync"/> before assuming the
    /// reminders followed it.</para>
    /// </summary>
    /// <para><b>T-060:</b> now an administrator-editable setting as FR-ADM-006 requires, not only an
    /// appsettings key. The reader keeps configuration as the fallback when no row exists, so a
    /// deployment that set this in appsettings on purpose is not reset by the table appearing.</para>
    private Task<int> ExpiringSoonWindowDaysAsync(CancellationToken ct) =>
        settings.GetIntAsync(SystemSettings.ExpiringSoonWindowDays, ct);

    /// <summary>
    /// BRULE-025's cadence, marked `[ASSUMPTION]` in BUSINESS-RULES.md - the Ministry has not
    /// confirmed 30/14/3. Configurable for that reason: when they decide, it is a setting change
    /// rather than a deploy, and the reminder ledger keys on the threshold value itself so a change
    /// cannot re-interpret reminders already sent.
    ///
    /// <para><b>Independent of ExpiringSoonWindowDaysAsync, deliberately.</b> The state boundary and the
    /// communication schedule are different questions: the state is what the system believes, the
    /// ladder is what the supplier has been told. Two consequences, both intended, neither obvious:</para>
    /// <list type="bullet">
    /// <item>A window WIDER than the top rung (say 45 against 30) leaves the document sitting in
    /// ExpiringSoon for fifteen days before the first email. The state is ahead of the conversation.
    /// If that silence is unwanted, the fix is to add a rung, not to widen the ladder implicitly -
    /// a reminder schedule should be a list of decisions, not a side effect of a threshold.</item>
    /// <item>A window NARROWER than the top rung (say 14 against 30) fires the 30-day rung while the
    /// document is still Approved. That is why Approved is in the candidate filter below and not
    /// only ExpiringSoon: dropping it would silently delete the supplier's first reminder whenever
    /// someone tightened the window, which is the kind of loss nobody would attribute to the setting
    /// they changed.</item>
    /// </list>
    /// </summary>
    private Task<int[]> ReminderThresholdDaysAsync(CancellationToken ct) =>
        settings.GetIntListAsync(SystemSettings.RenewalReminderDays, ct);

    public async Task RunAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.Date);
        var soonThreshold = today.AddDays(await ExpiringSoonWindowDaysAsync(ct));

        var expiringSoon = await db.SupplierDocuments
            .Where(d => d.IsLatestVersion && d.State == DocumentState.Approved && d.ExpiryDate != null && d.ExpiryDate <= soonThreshold)
            .ToListAsync(ct);

        foreach (var doc in expiringSoon)
        {
            doc.MarkExpiringSoon();
            await auditLogger.LogAsync("SupplierDocument", doc.Id, "document_expiring_soon", referenceCode: doc.ReferenceCode, ct: ct);
        }

        var expired = await db.SupplierDocuments
            .Where(d => d.IsLatestVersion && (d.State == DocumentState.Approved || d.State == DocumentState.ExpiringSoon) && d.ExpiryDate != null && d.ExpiryDate < today)
            .ToListAsync(ct);

        foreach (var doc in expired)
        {
            doc.MarkExpired();
            await auditLogger.LogAsync("SupplierDocument", doc.Id, "document_expired", referenceCode: doc.ReferenceCode, ct: ct);
        }

        await AutoSuspendForAwardCriticalExpiryAsync(expired, ct);

        var reminders = await DecideRemindersAsync(today, now, ct);

        if (expiringSoon.Count > 0 || expired.Count > 0 || reminders.Count > 0)
        {
            // The ledger rows and the state changes commit together. If the process dies after the
            // email is enqueued but before the ledger is written, the supplier gets one duplicate;
            // if it dies the other way round, they get silence. Committing first and enqueuing after
            // chooses the duplicate, which is the recoverable failure.
            await db.SaveChangesAsync(ct);
        }

        // The Approved -> ExpiringSoon transition deliberately sends nothing of its own. It used to,
        // and keeping that would have emailed twice on the same run for the same document: once for
        // crossing the state boundary and once for crossing the 30-day cadence step, which are the
        // same event described two ways. The cadence owns every "your document is expiring" message;
        // the transition owns the state and its audit entry.
        foreach (var doc in expired)
        {
            await NotifyAsync(doc, (userId, documentId) =>
                backgroundJobs.Enqueue<EmailJobs>(job => job.SendDocumentExpiredEmailAsync(userId, documentId, CancellationToken.None)), ct);
        }

        foreach (var doc in reminders)
        {
            await NotifyAsync(doc, (userId, documentId) =>
                backgroundJobs.Enqueue<EmailJobs>(job => job.SendDocumentExpiringEmailAsync(userId, documentId, CancellationToken.None)), ct);
        }
    }

    /// <summary>
    /// BRULE-023: expiry of an award-critical document suspends the supplier.
    ///
    /// <para>Driven entirely by <see cref="Domain.ReferenceData.DocumentType.IsAwardCritical"/>,
    /// which no seeded type sets. The predicate is deliberately the narrowest thing that can express
    /// the rule - a single flag on the type - rather than anything inferred from IsRequired or
    /// ExpiryTracked. A predicate that guesses would suspend suppliers the Ministry never decided
    /// to suspend, and "was blocked from participating for a fortnight" is not undone by
    /// reactivation.</para>
    ///
    /// <para><b>Idempotent without needing to be.</b> Only documents this run transitioned to
    /// Expired are considered, and a document expires once. Re-running the job cannot re-suspend,
    /// and a supplier already Suspended or Deactivated is skipped rather than throwing - the
    /// document's expiry is a fact regardless of whether the supplier was available to act on.</para>
    /// </summary>
    private async Task AutoSuspendForAwardCriticalExpiryAsync(
        List<SupplierDocument> expired, CancellationToken ct)
    {
        if (expired.Count == 0) return;

        var expiredTypeIds = expired.Select(d => d.DocumentTypeId).Distinct().ToList();

        var awardCritical = await db.DocumentTypes
            .Where(t => expiredTypeIds.Contains(t.Id) && t.IsAwardCritical)
            .ToDictionaryAsync(t => t.Id, t => t.Code, ct);

        if (awardCritical.Count == 0) return;

        foreach (var doc in expired.Where(d => awardCritical.ContainsKey(d.DocumentTypeId)))
        {
            var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == doc.SupplierId, ct);

            if (supplier is null || supplier.LifecycleState != SupplierLifecycleState.Active) continue;

            // InvariantCulture is not decoration. Interpolating a date under an Arabic-locale host
            // uses Umm al-Qura, which supports only 1900-2077 - a past expiry outside that range
            // threw from inside the message's own construction once already in this codebase, and
            // here it would take down the whole job rather than one request.
            var reason = string.Format(
                CultureInfo.InvariantCulture,
                "Automatic suspension (BRULE-023): award-critical document '{0}' expired on {1:yyyy-MM-dd}.",
                awardCritical[doc.DocumentTypeId], doc.ExpiryDate!.Value.ToDateTime(TimeOnly.MinValue));

            supplier.Suspend(reason);

            // The reason goes on the audit row as well as into the domain call. A suspension whose
            // record says only "suspended" leaves the supplier's support conversation starting from
            // nothing, and this is the one suspension nobody can be asked to explain.
            await auditLogger.LogAsync(
                "Supplier", supplier.Id, "supplier_auto_suspended",
                actorLabel: "system",
                fromState: nameof(SupplierLifecycleState.Active),
                toState: nameof(SupplierLifecycleState.Suspended),
                reason: reason, ct: ct);
        }
    }

    /// <summary>
    /// Works out which cadence steps a document has newly crossed, writes the ledger rows, and says
    /// which of them warrant an email.
    ///
    /// <para>A document first seen with three days left has crossed 30, 14 and 3 simultaneously -
    /// on a first deployment, or after a job outage, that is the normal case rather than an exotic
    /// one. Sending three emails is absurd; sending one per day for the next three days is worse,
    /// because it chases a deadline that has already effectively arrived. So every newly crossed
    /// step is RECORDED, and only the most urgent one is SENT. The ledger then reflects what the
    /// supplier actually received.</para>
    /// </summary>
    private async Task<List<SupplierDocument>> DecideRemindersAsync(
        DateOnly today, DateTimeOffset now, CancellationToken ct)
    {
        var thresholds = await ReminderThresholdDaysAsync(ct);
        var widest = thresholds.Max();
        var horizon = today.AddDays(widest);

        // Still-live documents only. An expired document is chased by BRULE-023's suspension path,
        // not by renewal reminders, and a rejected one has a different conversation attached to it.
        //
        // Approved is included on purpose, not incidentally: see ReminderThresholdDaysAsync. A rung can
        // fall due before the document has crossed into ExpiringSoon, and it should still be sent.
        var candidates = await db.SupplierDocuments
            .Where(d => d.IsLatestVersion
                && (d.State == DocumentState.Approved || d.State == DocumentState.ExpiringSoon)
                && d.ExpiryDate != null
                && d.ExpiryDate >= today
                && d.ExpiryDate <= horizon)
            .ToListAsync(ct);

        if (candidates.Count == 0) return [];

        var candidateIds = candidates.Select(d => d.Id).ToList();
        var alreadyRecorded = await db.DocumentExpiryReminders
            .Where(r => candidateIds.Contains(r.SupplierDocumentId))
            .Select(r => new { r.SupplierDocumentId, r.DocumentVersion, r.ThresholdDays })
            .ToListAsync(ct);

        var recorded = alreadyRecorded
            .Select(r => (r.SupplierDocumentId, r.DocumentVersion, r.ThresholdDays))
            .ToHashSet();

        var toNotify = new List<SupplierDocument>();

        foreach (var doc in candidates)
        {
            var daysRemaining = doc.ExpiryDate!.Value.DayNumber - today.DayNumber;

            var newlyCrossed = thresholds
                .Where(t => daysRemaining <= t)
                .Where(t => !recorded.Contains((doc.Id, doc.Version, t)))
                .ToList();

            if (newlyCrossed.Count == 0) continue;

            // Most urgent = smallest threshold. It is sent; the wider ones it overtook are recorded
            // as passed so they cannot fire later as a backlog.
            var mostUrgent = newlyCrossed.Min();

            foreach (var threshold in newlyCrossed)
            {
                db.DocumentExpiryReminders.Add(DocumentExpiryReminder.Record(
                    doc.Id, doc.Version, threshold, wasSent: threshold == mostUrgent, now));
            }

            toNotify.Add(doc);
        }

        return toNotify;
    }

    /// <summary>
    /// The single point where a document event becomes a message to a supplier.
    ///
    /// <para>BRULE-025 asks for email <b>and in-app</b>. In-app notifications have no store, no
    /// read/unread model and no endpoint yet, so building half of one here would be worse than
    /// leaving the seam visible: this method is where the second channel attaches, and it is the
    /// only place that needs to change when it exists.</para>
    ///
    /// <para>MSP-89 landed here as predicted: the job arguments are now a user id and a document id,
    /// and the address and filename are resolved inside the job so neither reaches the Hangfire
    /// store.</para>
    /// </summary>
    private async Task NotifyAsync(SupplierDocument doc, Action<Guid, Guid> enqueueEmail, CancellationToken ct)
    {
        var userId = await db.Users
            .Where(u => u.SupplierId == doc.SupplierId)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct);

        if (userId is not null) enqueueEmail(userId.Value, doc.Id);
    }
}
