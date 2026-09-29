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
// THE CATEGORY CANNOT BE TRANSLATED AND IS LEFT UNSET. The ERP's supplier groups are its own purchasing vocabulary
// - مواد غذائية, مستلزمات فندقية - and the portal's categories are the ministry's tourism taxonomy. There is no
// honest mapping, and a guess would be indistinguishable from a real choice once written. The group itself is kept
// on the supplier exactly as the ERP wrote it, so nothing the ERP knows is lost while the ministry decides.
//
// WHAT THE ADDRESS, THE ARABIC NAME AND THE CONTACT PERSON BECAME is said by ErpImportAdmission, and the address's fate
// and the registration number's by the rules both the preview and the run call; the run reports the same notes. This
// adds only the group and phone notes, and the tax-number match only the preview looks for.
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

public sealed record ErpImportCandidateMatch(
    string ReferenceCode,
    string? TaxId,
    int AddressCount = 0,
    string? BlockedByState = null);

public static class ErpImportPreviewBuilder
{
    public static ErpImportPreviewReport Build(
        IReadOnlyList<ErpSupplier> erpSuppliers,
        IReadOnlyDictionary<string, ErpImportCandidateMatch> byExternalId,
        IReadOnlyDictionary<string, string> unlinkedByTaxId,
        IReadOnlyDictionary<string, RegistrationNumberHolder>? registrationNumbersInPortal = null)
    {
        var registrationNumbers = ErpRegistrationNumbers.Decide(
            erpSuppliers,
            registrationNumbersInPortal ?? new Dictionary<string, RegistrationNumberHolder>(StringComparer.Ordinal));

        var rows = erpSuppliers
            .Select(supplier => Row(supplier, byExternalId, unlinkedByTaxId, registrationNumbers[supplier.ExternalId]))
            .ToList();

        return new ErpImportPreviewReport(
            erpSuppliers.Count,
            rows.Count(r => r.Action == ErpImportAction.Create),
            rows.Count(r => r.Action == ErpImportAction.Update),
            rows.Count(r => r.Action == ErpImportAction.Refuse),
            rows);
    }

    private static ErpImportPreviewRow Row(
        ErpSupplier supplier,
        IReadOnlyDictionary<string, ErpImportCandidateMatch> byExternalId,
        IReadOnlyDictionary<string, string> unlinkedByTaxId,
        ErpRegistrationNumberDecision registrationNumber)
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

        notes.Add(
            supplier.SupplierGroup is null
                ? "No supplier group; the category is left for the ministry to classify."
                : $"The ERP group '{supplier.SupplierGroup}' is kept as the supplier's group; it has no portal "
                  + "category, so the category is left for the ministry to classify.");

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
