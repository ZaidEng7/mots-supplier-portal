// The nightly job that moves documents towards expiry and chases the supplier to renew them.
//
// It moves an approved document into expiring-soon once it falls inside a configurable window, moves it to
// expired on the day, and sends renewal reminders on an escalating schedule.
//
//
// ASSUME THIS RUNS MORE THAN ONCE A DAY
//
// Retries, host restarts, manual triggers and schedule changes all re-run it. Nothing here is keyed on the
// run. Everything is keyed on what has already been communicated about a given version of a document.
//
//
// WHY THE REMINDERS ARE WRITTEN DOWN
//
// De-duplication used to be an accident of the state machine: the transition throws once a document is
// already expiring-soon, so the job could only ever notify once. It behaved correctly for the wrong reason,
// and the same accident made an escalating schedule impossible, because escalation needs to notify more
// than once and the guard providing the de-duplication forbade it.
//
// Reminders are now recorded in their own table, so escalation and de-duplication stop being in tension.
//
//
// THE WINDOW AND THE LADDER ARE TWO DIFFERENT NUMBERS
//
// The window decides when a document ENTERS expiring-soon. The ladder decides when the supplier is TOLD.
// Both are administrator-editable settings rather than constants, because the written requirement calls
// both configurable and a constant with a comment claiming otherwise is an artefact asserting something
// untrue.
//
// They coincide only at their shared default of thirty days, which is exactly why the coupling is easy to
// assume and worth stating. Two consequences, both intended:
//
// A window WIDER than the top rung, say forty-five against thirty, leaves the document sitting in
// expiring-soon for fifteen days before the first email. The state is ahead of the conversation. If that
// silence is unwanted the fix is to add a rung, not to widen the ladder implicitly: a reminder schedule
// should be a list of decisions rather than a side effect of a threshold.
//
// A window NARROWER than the top rung, say fourteen against thirty, fires the thirty-day rung while the
// document is still approved. That is why approved documents are in the reminder candidate filter and not
// only expiring-soon ones. Dropping them would silently delete the supplier's first reminder whenever
// somebody tightened the window, which is the kind of loss nobody would attribute to the setting they
// changed.
//
// The ladder's own numbers are an assumption. The ministry has not confirmed thirty, fourteen and three,
// which is why they are configurable, and why the ledger keys on the threshold value itself so that
// changing the ladder cannot re-interpret reminders already sent.
//
//
// EVERY CROSSED RUNG IS RECORDED, ONLY THE MOST URGENT IS SENT
//
// A document first seen with three days left has crossed thirty, fourteen and three at once. On a first
// deployment, or after an outage, that is the normal case rather than an exotic one.
//
// Sending three emails is absurd, and sending one a day for the next three days is worse, because it
// chases a deadline that has already effectively arrived. So the wider rungs are written down as passed
// and only the nearest one is sent, and the ledger then reflects what the supplier actually received.
//
//
// THE STATE CHANGE ITSELF SENDS NOTHING
//
// Entering expiring-soon used to send its own email, and keeping that would have emailed twice on the same
// run for the same document: once for crossing the state boundary and once for crossing the thirty-day
// rung, which are the same event described two ways.
//
// The ladder owns every "your document is expiring" message. The transition owns the state and its audit
// entry.
//
//
// THE ORDER OF THE COMMIT AND THE EMAILS
//
// The ledger rows and the state changes commit together, and the emails are enqueued after. If the process
// dies after an email is enqueued but before the ledger is written, the supplier gets one duplicate; if it
// dies the other way round, they get silence. Committing first chooses the duplicate, which is the
// recoverable failure.
//
//
// SUSPENSION WHEN AN AWARD-CRITICAL DOCUMENT EXPIRES
//
// Driven entirely by the flag on the document type, which the shipped types set for two: an expired
// commercial register and an expired tax card.
//
// The test is deliberately the narrowest thing that can express the rule, a single flag, rather than
// anything inferred from whether a type is required or tracked for expiry. A test that guessed would
// suspend suppliers the ministry never decided to suspend, and "was blocked from bidding for a fortnight"
// is not undone by reactivating them.
//
// It is idempotent without needing to be: only documents this run moved to expired are considered, and a
// document expires once. A supplier already suspended or deactivated is skipped rather than throwing,
// because the document's expiry is a fact regardless of whether the supplier was available to act on it.
//
// The reason is written onto the audit row as well as passed to the domain, because a suspension whose
// record says only "suspended" leaves the supplier's support conversation starting from nothing, and this
// is the one suspension nobody can be asked to explain.
//
// The date inside that reason is formatted culture-invariantly, which is not decoration. Interpolating a
// date under an Arabic-locale host uses a calendar that covers only 1900 to 2077, and a past expiry
// outside that range once threw from inside a message's own construction elsewhere in this codebase. Here
// it would take down the whole job rather than one request.
//
//
// THE SEAM WHERE THE SECOND CHANNEL ATTACHES
//
// The rule asks for email and in-app notification. In-app notifications have no store, no read state and
// no endpoint yet, so building half of one here would be worse than leaving the seam visible. One method
// turns a document event into a message, and it is the only place that changes when the second channel
// exists.
//
// The enqueued job carries a user identifier and a document identifier. The address and the filename are
// resolved inside the job, so neither reaches the background-job store.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

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

public sealed class DocumentExpiryJob(
    AppDbContext db,
    IAuditLogger auditLogger,
    IBackgroundJobClient backgroundJobs,
    ISystemSettingReader settings)
{
    private Task<int> ExpiringSoonWindowDaysAsync(CancellationToken ct) =>
        settings.GetIntAsync(SystemSettings.ExpiringSoonWindowDays, ct);

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
            await db.SaveChangesAsync(ct);
        }

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

            var reason = string.Format(
                CultureInfo.InvariantCulture,
                "Automatic suspension (BRULE-023): award-critical document '{0}' expired on {1:yyyy-MM-dd}.",
                awardCritical[doc.DocumentTypeId], doc.ExpiryDate!.Value.ToDateTime(TimeOnly.MinValue));

            supplier.Suspend(reason);

            await auditLogger.LogAsync(
                "Supplier", supplier.Id, "supplier_auto_suspended",
                actorLabel: "system",
                fromState: nameof(SupplierLifecycleState.Active),
                toState: nameof(SupplierLifecycleState.Suspended),
                reason: reason, ct: ct);
        }
    }

    private async Task<List<SupplierDocument>> DecideRemindersAsync(
        DateOnly today, DateTimeOffset now, CancellationToken ct)
    {
        var thresholds = await ReminderThresholdDaysAsync(ct);
        var widest = thresholds.Max();
        var horizon = today.AddDays(widest);

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

    private async Task NotifyAsync(SupplierDocument doc, Action<Guid, Guid> enqueueEmail, CancellationToken ct)
    {
        var userId = await db.Users
            .Where(u => u.SupplierId == doc.SupplierId)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct);

        if (userId is not null) enqueueEmail(userId.Value, doc.Id);
    }
}
