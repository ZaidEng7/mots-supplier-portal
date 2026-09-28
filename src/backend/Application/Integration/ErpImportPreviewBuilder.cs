// Deciding, for each supplier the ERP sent, what importing it would do.
//
// THIS IS PURE ON PURPOSE. Every rule worth arguing about lives here or in ErpImportAdmission - how gaps are
// filled, what is worth telling somebody, how a row is matched to one the portal already has - and none of it
// needs a database to exercise. The handler around it does two reads and calls this.
//
//
// NO SUPPLIER IS REFUSED. The portal is meant to show the whole of Seven Gates' supplier base, so a supplier with
// no email gets a placeholder, an unknown currency is left empty, and a disabled one arrives suspended. How each
// gap is filled, and why, lives in ErpImportAdmission, which the import itself also uses: two copies of those
// rules would drift, and a forecast that disagrees with the outcome is worse than none, because it was believed.
//
// THE NOTES ARE THE DELIVERABLE. With nothing refused, the count of creates says how big the job is and the notes
// say what is being agreed to - which suppliers have a placeholder instead of a real address, which have no
// currency, which arrive suspended. A summary cannot carry that.
//
//
// WHAT IS NOTED BUT CANNOT BE FILLED
//
// THE CATEGORY CANNOT BE TRANSLATED AND IS LEFT UNSET. The ERP's supplier groups are its factory defaults -
// Distributor, Electrical, Raw Material, Local - and the portal's are the ministry's tourism taxonomy. There is
// no honest mapping, and a guess would be indistinguishable from a real choice once written.
//
// NO ADDRESS IS IMPORTED YET. A supplier's street lives in a separate record in the ERP and that half of the
// mapping has never been tested against real data. Saying so on every row is the point: it is a gap in what this
// import does, not in the supplier.
//
// AN ARABIC NAME IS NEVER PRESENT. The ERP has one name field, so the Arabic name starts as the English one.
//
//
// WHO IS HELD FOR A PERSON OR SUSPENDED IS DECIDED BY ErpSyncPlan, which the run calls too, on data read before the
// first write. The first version computed the suspension limit separately here and in the run, and the run's
// version counted that night's new suppliers - so this forecast could say "held back" while the run suspended.
//
// A SUPPLIER THE ERP NO LONGER RETURNS WOULD BE SUSPENDED. Seven Gates deletes by removing, so absence is the
// only signal a deletion leaves - and the same signal a broken read leaves, which is why ErpMissingSupplierPolicy
// decides whether to believe it. Suspended, not deactivated: visible, not invitable, and reversible by a person.
// A supplier that registered here itself carries no ERP identifier and is never a candidate.
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

public sealed record ErpImportCandidateMatch(
    string ReferenceCode,
    string? TaxId,
    string? Name = null,
    bool IsActive = true,
    string? LoginEmail = null,
    bool AlreadyRemovedFromErp = false);

public static class ErpImportPreviewBuilder
{
    public static ErpImportPreviewReport Build(
        IReadOnlyList<ErpSupplier> erpSuppliers,
        IReadOnlyDictionary<string, ErpImportCandidateMatch> byExternalId,
        IReadOnlyDictionary<string, string> unlinkedByTaxId)
    {
        var plan = ErpSyncPlan.Build(erpSuppliers, [.. byExternalId.Select(pair => new PortalLinkedSupplier(
            pair.Key,
            pair.Value.ReferenceCode,
            pair.Value.Name,
            pair.Value.TaxId,
            pair.Value.LoginEmail,
            pair.Value.IsActive,
            pair.Value.AlreadyRemovedFromErp))]);

        var heldFrom = plan.ProbableRenames.ToDictionary(r => r.NewExternalId, r => r, StringComparer.Ordinal);

        var rows = erpSuppliers
            .Select(supplier => heldFrom.TryGetValue(supplier.ExternalId, out var held)
                ? HeldRow(supplier, held)
                : Row(supplier, byExternalId, unlinkedByTaxId))
            .ToList();

        rows.AddRange(plan.ToSuspend.Select(missing => new ErpImportPreviewRow(
            missing.ExternalId,
            missing.Name ?? missing.ExternalId,
            ErpImportAction.Suspend,
            ["No longer in the ERP; would be suspended - kept in the registry, but cannot be invited to tenders."],
            missing.ReferenceCode)));

        return new ErpImportPreviewReport(
            erpSuppliers.Count,
            rows.Count(r => r.Action == ErpImportAction.Create),
            rows.Count(r => r.Action == ErpImportAction.Update),
            rows.Count(r => r.Action == ErpImportAction.Refuse),
            rows,
            rows.Count(r => r.Action == ErpImportAction.Suspend),
            plan.SuspensionsHeldBack);
    }

    // What a person is told about a probable rename, in the preview and in the run alike.
    //
    // It says what was NOT done, because the reader's first question is whether anything is broken: nothing was
    // suspended and nothing was created, and the existing supplier carries on as before. It names the signal, because
    // "the same tax number" and "the same sign-in address" call for different checks. And it says plainly that the
    // portal will not decide this, so nobody waits for the next run to sort it out.
    public static string ProbableRenameNote(ErpProbableRename held) =>
        $"Possibly the same company as {held.ReferenceCode} ('{held.OldExternalId}' in the ERP, which no longer "
        + $"returns it) - they share {held.Signal}. Not created, and {held.ReferenceCode} was not suspended: it carries "
        + "on as before. The portal will not decide whether these are one company, because a wrong guess would move one "
        + "company's history onto another. Check with Seven Gates.";

    private static ErpImportPreviewRow HeldRow(ErpSupplier supplier, ErpProbableRename held) => new(
        supplier.ExternalId,
        ErpImportAdmission.Admit(supplier).Name,
        ErpImportAction.Refuse,
        [ProbableRenameNote(held)],
        held.ReferenceCode);

    private static ErpImportPreviewRow Row(
        ErpSupplier supplier,
        IReadOnlyDictionary<string, ErpImportCandidateMatch> byExternalId,
        IReadOnlyDictionary<string, string> unlinkedByTaxId)
    {
        var admitted = ErpImportAdmission.Admit(supplier);
        var notes = new List<string>(admitted.Notes);

        notes.Add(
            supplier.SupplierGroup is null
                ? "No supplier group; the category is left for the ministry to classify."
                : $"The ERP group '{supplier.SupplierGroup}' has no portal category; the category is left for "
                  + "the ministry to classify.");

        if (supplier.PrimaryAddressName is null)
        {
            notes.Add("No address; city, governorate and coordinates stay empty.");
        }

        notes.Add("The Arabic name starts as the English one; the ERP holds only one name.");

        if (supplier.Phone is null)
        {
            notes.Add("No phone number.");
        }

        var matched = byExternalId.TryGetValue(supplier.ExternalId, out var existing) ? existing : null;

        if (matched is null
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
            matched is null ? ErpImportAction.Create : ErpImportAction.Update,
            notes,
            matched?.ReferenceCode);
    }
}
