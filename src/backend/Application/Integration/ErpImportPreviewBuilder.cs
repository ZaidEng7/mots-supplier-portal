// Deciding, for each supplier the ERP sent, what importing it would do.
//
// THIS IS PURE ON PURPOSE. Every rule worth arguing about lives here - what makes a supplier unusable, what is
// merely worth knowing, how a row is matched to one the portal already has - and none of it needs a database to
// exercise. The handler around it does two reads and calls this, which keeps the reads boring and the rules
// testable.
//
//
// THE TWO REFUSALS
//
// NO EMAIL IS THE ONE THAT MATTERS. Supplier.Register takes a representative email as a required argument, and
// an account somebody can sign into needs a mailbox for the password link to arrive at. A supplier with no email
// is therefore not a supplier this product can create, whatever else is known about them. It is also the field
// most likely to be missing in bulk, which is why the count of refusals is the number that tells an operator
// whether this import is worth running at all.
//
// A CURRENCY THE PORTAL DOES NOT KNOW REFUSES THE ROW rather than falling back to the local one. The portal
// knows SYP and USD. Defaulting an unrecognised currency to SYP would silently reprice a supplier's whole
// relationship, and the ministry's feed would report that price as fact. A refused row is a question; a
// defaulted one is a wrong answer nobody asked.
//
// A MISSING currency is NOT the same thing and is not refused - the portal's own field is optional, and a
// supplier who simply has not been given one is ordinary.
//
//
// WHAT IS A NOTE RATHER THAN A REFUSAL
//
// THE CATEGORY CANNOT BE TRANSLATED AND IS LEFT UNSET. The ERP's supplier groups are its factory defaults -
// Distributor, Electrical, Raw Material, Local - and the portal's are the ministry's tourism taxonomy. There is
// no honest mapping between those two lists, and inventing one would file every supplier under a category
// somebody would later have to correct without knowing it was a guess. Unset is recoverable; wrongly set is not,
// because nothing distinguishes it from a real choice.
//
// NO ADDRESS IS EXPECTED, NOT EXCEPTIONAL. A supplier's street lives in a separate record in the ERP, reachable
// only through a join table the current credential cannot read. Until that is granted, every supplier arrives
// with no address, which costs the ministry's feed its city, governorate and coordinates. Saying so on every row
// is the point: it is a gap in the data, not a gap in this code.
//
// AN ARABIC NAME IS NEVER PRESENT. The ERP has one name field. Every imported supplier's Arabic name starts as
// the English one, and somebody has to fix it by hand later.
//
//
// MATCHING
//
// THE ERP'S OWN IDENTIFIER IS THE MATCH, and nothing else is. A portal supplier carrying that identifier is the
// same supplier, and the import updates it; one that is not is new.
//
// A TAX NUMBER SHARED WITH A SUPPLIER THAT HAS NO ERP IDENTIFIER IS REPORTED, NOT MATCHED. That is almost
// certainly the same company having registered on the portal themselves, and merging them automatically would
// join two records on a field that is sometimes blank, sometimes mistyped, and occasionally shared between a
// company and its subsidiary. So it is flagged for a person, which is the only safe answer available.

namespace MotsSupplierPortal.Application.Integration;

public sealed record ErpImportCandidateMatch(string ReferenceCode, string? TaxId);

public static class ErpImportPreviewBuilder
{
    public static readonly IReadOnlyList<string> KnownCurrencies = ["SYP", "USD"];

    public static ErpImportPreviewReport Build(
        IReadOnlyList<ErpSupplier> erpSuppliers,
        IReadOnlyDictionary<string, ErpImportCandidateMatch> byExternalId,
        IReadOnlyDictionary<string, string> unlinkedByTaxId)
    {
        var rows = erpSuppliers
            .Select(supplier => Row(supplier, byExternalId, unlinkedByTaxId))
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
        IReadOnlyDictionary<string, string> unlinkedByTaxId)
    {
        var notes = new List<string>();
        var refusals = new List<string>();

        if (string.IsNullOrWhiteSpace(supplier.Name))
        {
            refusals.Add("The supplier has no name.");
        }

        if (supplier.Email is null)
        {
            refusals.Add(
                "The supplier has no email address, so no account can be created: a login needs a mailbox for "
                + "the password link.");
        }

        if (supplier.Currency is not null
            && !KnownCurrencies.Contains(supplier.Currency, StringComparer.OrdinalIgnoreCase))
        {
            refusals.Add(
                $"The currency '{supplier.Currency}' is not one the portal knows ({string.Join(", ", KnownCurrencies)}).");
        }

        if (supplier.Currency is null)
        {
            notes.Add("No currency; the supplier's currency is left unset.");
        }

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

        if (supplier.Disabled)
        {
            notes.Add("Disabled in the ERP; the supplier would be created deactivated.");
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

        var action = refusals.Count > 0
            ? ErpImportAction.Refuse
            : matched is null ? ErpImportAction.Create : ErpImportAction.Update;

        return new ErpImportPreviewRow(
            supplier.ExternalId,
            supplier.Name,
            action,
            [.. refusals, .. notes],
            matched?.ReferenceCode);
    }
}
