// Deciding, before anything is written, which ERP supplier may carry which registration number.
//
// THE PORTAL ALLOWS ONE SUPPLIER PER REGISTRATION NUMBER, compared trimmed, and enforces it with a unique index -
// that is how self-registration stops the same company signing up twice. The ERP enforces nothing of the kind, and
// the real one gives number 14142 to two different suppliers. Written straight through, the second would hit the
// index, fail, and not be imported at all: a supplier lost over one field.
//
// SO THE NUMBER IS DROPPED, NOT THE SUPPLIER. The first claimant in the ERP's own order keeps it; a later one arrives
// with the field empty and a note naming who holds it. A number already on a different portal supplier - one who
// registered here, or another ERP record - is not taken from them either. The supplier who already holds it keeps it,
// which is what makes a second run of the import a no-op rather than a string of refusals against itself.
//
// IT IS ONE PLACE FOR THE SAME REASON THE REST OF THE ADMISSION IS: the preview must promise exactly what the run
// does, so both ask this, with the portal's numbers read before the first write.

namespace MotsSupplierPortal.Application.Integration;

public sealed record RegistrationNumberHolder(string? ExternalId, string ReferenceCode);

public sealed record ErpRegistrationNumberDecision(string? Number, string? Note);

public static class ErpRegistrationNumbers
{
    public static IReadOnlyDictionary<string, ErpRegistrationNumberDecision> Decide(
        IReadOnlyList<ErpSupplier> erpSuppliers,
        IReadOnlyDictionary<string, RegistrationNumberHolder> heldInPortal)
    {
        var decisions = new Dictionary<string, ErpRegistrationNumberDecision>(StringComparer.Ordinal);
        var claimedTonight = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var supplier in erpSuppliers)
        {
            var number = Normalised(supplier.RegistrationNumber);

            if (number is null)
            {
                decisions[supplier.ExternalId] = new ErpRegistrationNumberDecision(null, null);
                continue;
            }

            if (number.Length > ErpFieldLimits.RegistrationNumber)
            {
                decisions[supplier.ExternalId] = new ErpRegistrationNumberDecision(
                    null,
                    $"The ERP's registration number is {number.Length} characters, longer than the portal's "
                    + $"{ErpFieldLimits.RegistrationNumber}; left empty rather than cut into a different number.");
                continue;
            }

            if (heldInPortal.TryGetValue(number, out var holder)
                && !string.Equals(holder.ExternalId, supplier.ExternalId, StringComparison.Ordinal))
            {
                decisions[supplier.ExternalId] = new ErpRegistrationNumberDecision(
                    null,
                    $"Registration number {number} is already on {holder.ReferenceCode}; left empty here, because the "
                    + "portal allows one supplier per registration number.");
                continue;
            }

            if (claimedTonight.TryGetValue(number, out var first))
            {
                decisions[supplier.ExternalId] = new ErpRegistrationNumberDecision(
                    null,
                    $"The ERP gives registration number {number} to '{first}' as well; left empty here, because the "
                    + "portal allows one supplier per registration number.");
                continue;
            }

            claimedTonight[number] = supplier.Name ?? supplier.ExternalId;
            decisions[supplier.ExternalId] = new ErpRegistrationNumberDecision(number, null);
        }

        return decisions;
    }

    public static string? Normalised(string? number) => string.IsNullOrWhiteSpace(number) ? null : number.Trim();
}
