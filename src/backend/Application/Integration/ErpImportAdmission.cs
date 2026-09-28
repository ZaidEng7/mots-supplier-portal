// Whether the portal can represent a supplier the ERP sent, and why not when it cannot.
//
// THIS IS ONE PLACE BECAUSE IT IS ASKED TWICE. The preview forecasts what the import would do, and the import
// then does it. If those two carried their own copies of the rules, the day they drifted is the day the preview
// promises eighty accounts and the run produces sixty - and the preview is the thing somebody read before
// agreeing. A forecast that disagrees with the outcome is worse than no forecast, because it was believed.
//
// NO EMAIL IS THE REFUSAL THAT MATTERS. Supplier.Register requires a representative email, and an account
// somebody can sign into needs an address to reach them at. It is also the field most likely to be missing in
// bulk, which is why its count is the number that says whether an import is worth running at all.
//
// A CURRENCY THE PORTAL DOES NOT KNOW REFUSES THE ROW rather than defaulting to the local one. Defaulting would
// silently reprice a supplier's whole relationship and the ministry's feed would then report that price as fact.
// A refused row is a question; a defaulted one is a wrong answer nobody asked for.
//
// A MISSING CURRENCY IS NOT THE SAME THING. The portal's own field is optional, and a supplier nobody has given
// one is ordinary rather than broken.

namespace MotsSupplierPortal.Application.Integration;

public static class ErpImportAdmission
{
    public static readonly IReadOnlyList<string> KnownCurrencies = ["SYP", "USD"];

    public static IReadOnlyList<string> Refusals(ErpSupplier supplier)
    {
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
                $"The currency '{supplier.Currency}' is not one the portal knows "
                + $"({string.Join(", ", KnownCurrencies)}).");
        }

        return refusals;
    }
}
