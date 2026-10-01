// Deciding, for each supplier the ERP sent, what importing it would do.
//
// THIS IS PURE ON PURPOSE, so every rule it applies can be exercised without a database. PreviewErpImportHandler
// reads the portal - its ERP-linked suppliers through LinkedSuppliersInPortal, the reader the run uses too - and the
// ERP, and calls this.
//
// THE PREVIEW AND THE RUN SHARE EVERY DECISION, on data read before the first write, because a forecast that disagrees
// with the outcome is worse than none: it was believed. Both call the same rules:
//   ErpImportAdmission       how each gap in the ERP's record is filled, and what becomes of its address
//   ErpRegistrationNumbers   which supplier may carry which registration number
//   ErpSyncPlan              which suppliers are held for a person, suspended or marked as gone
//   ErpStandingDecision      what the ERP's standing does to a supplier the portal already holds, and its notes
// What this adds for the preview alone is the group and phone notes, the tax-number match, and the counts.
//
// TWO KINDS OF SUPPLIER ARE REFUSED, both decided by ErpSyncPlan and refused the same way by the run: a probable
// rename, held for a person, and an ERP supplier the portal's own push created and has not linked yet, left to the
// push. Every other gap is filled - a supplier with no email gets a placeholder, an unknown currency is left empty, and
// one the ERP disables or has not approved arrives suspended. The run can also refuse a supplier whose address
// already belongs to another account, which only the run checks, because only the run creates accounts.
//
// THE NOTES ARE THE DELIVERABLE. The count of creates says how big the job is and the notes say what is being agreed
// to - which suppliers have a placeholder instead of a real address, which have no currency, which arrive suspended.
// A summary cannot carry that.
//
//
// WHAT IS NOTED BUT CANNOT BE FILLED
//
// THE CATEGORY CANNOT BE TRANSLATED AND IS LEFT UNSET. The ERP's supplier groups are its own purchasing vocabulary
// - مواد غذائية, مستلزمات فندقية - and the portal's categories are the ministry's tourism taxonomy. There is no
// honest mapping, and a guess would be indistinguishable from a real choice once written. The group itself is kept
// on the supplier as the ERP wrote it, so nothing the ERP knows is lost while the ministry decides.
//
// WHAT THE ADDRESS, THE ARABIC NAME AND THE CONTACT PERSON BECAME is said by ErpImportAdmission, and the registration
// number's fate by ErpRegistrationNumbers; the run reports the same notes.
//
//
// SUSPENSIONS
//
// WHO IS HELD FOR A PERSON, SUSPENDED OR MARKED IS DECIDED BY ErpSyncPlan, and the run builds the same plan from the
// same read. A matched supplier the ERP now turns away is a Suspend row when ErpStandingDecision says the run would
// suspend it, and an Update row otherwise.
//
// A SUPPLIER THE ERP NO LONGER RETURNS WOULD BE SUSPENDED. Seven Gates deletes by removing, so absence is the
// only signal a deletion leaves - and the same signal a broken read leaves, which is why ErpMissingSupplierPolicy
// decides whether to believe it. Suspended, not deactivated: visible, not invitable, and reversible by a person.
// A supplier that registered here itself carries no ERP identifier and is never a candidate.
//
//
// MATCHING
//
// THE ERP'S OWN IDENTIFIER IS THE MATCH, and nothing else is. A portal supplier carrying that identifier is the
// same supplier, and the import updates it; one that is not is new.
//
// A TAX NUMBER SHARED WITH A SUPPLIER THAT HAS NO ERP IDENTIFIER IS REPORTED, NOT MATCHED. That is almost
// certainly the same company having registered on the portal themselves, and merging automatically would join two
// records on a field that is sometimes blank, sometimes mistyped, and occasionally shared between a company and
// its subsidiary. So it is flagged for a person.

namespace MotsSupplierPortal.Application.Integration;

using MotsSupplierPortal.Domain.Suppliers;

// A portal supplier the ERP's identifier matches, as the preview needs it: what ErpSyncPlan reads, plus what only the
// preview reports. LinkedSuppliersInPortal builds it with every field named; the defaults are for tests, so a field
// added here is read there too, or every preview silently takes its default.
public sealed record ErpImportCandidateMatch(
    string ReferenceCode,
    string? TaxId,
    string? Name = null,
    bool IsActive = true,
    string? LoginEmail = null,
    bool SuspendedAsRemovedFromErp = false,
    bool MarkedRemovedFromErp = false,
    int AddressCount = 0,
    string? BlockedByState = null,
    SupplierErpDisabledState ErpDisabledState = SupplierErpDisabledState.NotDisabled,
    bool AwaitsDocumentRenewal = false,
    bool PushedByPortal = false,
    bool SeenByImport = true);

public static class ErpImportPreviewBuilder
{
    public static ErpImportPreviewReport Build(
        IReadOnlyList<ErpSupplier> erpSuppliers,
        IReadOnlyDictionary<string, ErpImportCandidateMatch> byExternalId,
        IReadOnlyDictionary<string, string> unlinkedByTaxId,
        IReadOnlyDictionary<string, RegistrationNumberHolder>? registrationNumbersInPortal = null)
    {
        var plan = ErpSyncPlan.Build(erpSuppliers, [.. byExternalId.Select(pair => new PortalLinkedSupplier(
            ExternalId: pair.Key,
            ReferenceCode: pair.Value.ReferenceCode,
            Name: pair.Value.Name,
            TaxId: pair.Value.TaxId,
            LoginEmail: pair.Value.LoginEmail,
            IsActive: pair.Value.IsActive,
            SuspendedAsRemovedFromErp: pair.Value.SuspendedAsRemovedFromErp,
            MarkedRemovedFromErp: pair.Value.MarkedRemovedFromErp,
            ErpDisabledState: pair.Value.ErpDisabledState,
            PushedByPortal: pair.Value.PushedByPortal,
            SeenByImport: pair.Value.SeenByImport))]);

        var renamedFrom = plan.ProbableRenames.ToDictionary(r => r.NewExternalId, r => r, StringComparer.Ordinal);

        var registrationNumbers = ErpRegistrationNumbers.Decide(
            erpSuppliers,
            registrationNumbersInPortal ?? new Dictionary<string, RegistrationNumberHolder>(StringComparer.Ordinal));

        var rows = erpSuppliers
            .Select(supplier => plan.IsHeldForPush(supplier.ExternalId)
                ? HeldForPushRow(supplier)
                : renamedFrom.TryGetValue(supplier.ExternalId, out var rename)
                    ? ProbableRenameRow(supplier, rename)
                    : Row(supplier, byExternalId, unlinkedByTaxId, registrationNumbers[supplier.ExternalId], plan))
            .ToList();

        rows.AddRange(plan.ToSuspend.Select(missing => new ErpImportPreviewRow(
            missing.ExternalId,
            missing.Name ?? missing.ExternalId,
            ErpImportAction.Suspend,
            ["No longer in the ERP; would be suspended - kept in the registry, but cannot be invited to tenders."],
            missing.ReferenceCode)));

        return new ErpImportPreviewReport(
            ErpSupplierCount: erpSuppliers.Count,
            WouldCreate: rows.Count(r => r.Action == ErpImportAction.Create),
            WouldUpdate: rows.Count(r => r.Action == ErpImportAction.Update),
            Refused: rows.Count(r => r.Action == ErpImportAction.Refuse),
            Rows: rows,
            WouldSuspend: rows.Count(r => r.Action == ErpImportAction.Suspend),
            SuspensionsHeldBack: plan.SuspensionsHeldBack);
    }

    // What a person is told about a probable rename, in the preview and in the run alike.
    //
    // It says what was NOT done, because the reader's first question is whether anything is broken: nothing was
    // suspended and nothing was created, and the existing supplier carries on as before. It names the signal, because
    // "the same tax number" and "the same sign-in address" call for different checks. And it says plainly that the
    // portal will not decide this, so nobody waits for the next run to sort it out.
    public static string ProbableRenameNote(ErpProbableRename rename) =>
        $"Possibly the same company as {rename.ReferenceCode} ('{rename.OldExternalId}' in the ERP, which no "
        + $"longer returns it) - they share {rename.Signal}. Not created, and {rename.ReferenceCode} was not "
        + "suspended: it carries on as before. The portal will not decide whether these are one company, because a "
        + "wrong guess would move one company's history onto another. Check with Seven Gates.";

    // What a person is told about an ERP supplier the portal's own push created and has not linked yet, in the preview
    // and in the run alike. It says it was not created and why, and where to look if it stays unlinked, because the
    // push's own state is on the supplier's review page and nowhere in this report.
    public const string HeldForPushNote =
        "Created in the ERP by the portal's own push (the portal's API user owns it), and not carried by any portal "
        + "supplier yet, so it is not imported: the push links it to the supplier it was made for on its next attempt. "
        + "If it is still here after that, the supplier's ERP push on its review page says why.";

    private static ErpImportPreviewRow HeldForPushRow(ErpSupplier supplier) => new(
        supplier.ExternalId,
        ErpImportAdmission.Admit(supplier).Name,
        ErpImportAction.Refuse,
        [HeldForPushNote],
        null);

    private static ErpImportPreviewRow ProbableRenameRow(ErpSupplier supplier, ErpProbableRename rename) => new(
        supplier.ExternalId,
        ErpImportAdmission.Admit(supplier).Name,
        ErpImportAction.Refuse,
        [ProbableRenameNote(rename)],
        rename.ReferenceCode);

    // One ERP supplier that is not a probable rename: a Create row if the portal does not hold it yet, otherwise an
    // Update row, or a Suspend row when ErpStandingDecision says the run would suspend it.
    private static ErpImportPreviewRow Row(
        ErpSupplier supplier,
        IReadOnlyDictionary<string, ErpImportCandidateMatch> byExternalId,
        IReadOnlyDictionary<string, string> unlinkedByTaxId,
        ErpRegistrationNumberDecision registrationNumber,
        ErpSyncPlan plan)
    {
        var admitted = ErpImportAdmission.Admit(supplier);
        var notes = new List<string>(admitted.Notes);

        if (registrationNumber.Note is not null)
        {
            notes.Add(registrationNumber.Note);
        }

        var candidate = byExternalId.TryGetValue(supplier.ExternalId, out var found) ? found : null;
        notes.Add(ErpImportAdmission.AddressOutcome(
            admitted, candidate is null, candidate?.AddressCount ?? 0, candidate?.BlockedByState).Note);

        var decision = candidate is null
            ? null
            : ErpStandingDecision.Decide(
                memory: candidate.ErpDisabledState,
                isActive: candidate.IsActive,
                standing: admitted.Standing,
                turnedAway: admitted.TurnedAway,
                documentsAllowRelease: !candidate.AwaitsDocumentRenewal,
                planHoldsTurnedAway: plan.HoldsTurnedAwayFor(supplier.ExternalId),
                markedRemovedFromErp: candidate.MarkedRemovedFromErp);

        if (decision is null && admitted.ArrivalNote is not null)
        {
            notes.Add(admitted.ArrivalNote);
        }

        if (decision is not null)
        {
            notes.AddRange(decision.Notes);
        }

        notes.Add(
            supplier.SupplierGroup is null
                ? "No supplier group; the category is left for the ministry to classify."
                : $"The ERP group '{supplier.SupplierGroup}' is kept as the supplier's group; it has no portal "
                  + "category, so the category is left for the ministry to classify.");

        if (supplier.Phone is null)
        {
            notes.Add("No phone number.");
        }

        if (candidate is null
            && supplier.TaxId is not null
            && unlinkedByTaxId.TryGetValue(supplier.TaxId, out var duplicate))
        {
            notes.Add(
                $"Tax number {supplier.TaxId} is already on {duplicate}, which carries no ERP identifier - "
                + "probably the same company having registered on the portal. Needs a person to decide.");
        }

        return new ErpImportPreviewRow(
            supplier.ExternalId,
            admitted.Name,
            decision is null
                ? ErpImportAction.Create
                : decision.Change == ErpDisabledChange.Suspended ? ErpImportAction.Suspend : ErpImportAction.Update,
            notes,
            candidate?.ReferenceCode);
    }
}
