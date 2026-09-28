// Turning whatever the ERP sent into a supplier the portal can hold.
//
// EVERY SUPPLIER GETS IN. The first version refused a supplier with no email, an unknown currency or no name,
// which is the careful answer for an import and the wrong one for this ministry: the point is that the portal shows
// the whole of Seven Gates' supplier base, and a supplier left out is a supplier nobody can see, invite or
// correct. So gaps are filled with values that are visibly placeholders, and every one is noted, rather than the
// supplier being dropped.
//
// THIS IS ONE PLACE BECAUSE IT IS ASKED TWICE. The preview forecasts what the import will do and the import then
// does it. Two copies would drift, and the day they drift is the day the forecast promises one thing and the run
// does another - which is worse than no forecast, because it was believed. That already happened once, over
// disabled suppliers, and it is why this file now decides the lifecycle too.
//
//
// THE PLACEHOLDER EMAIL
//
// ON THE .invalid DOMAIN, which the internet's own standards reserve so that it can never deliver. An account
// created on it can receive nothing: no password link, no tender invitation, nothing that could reach a stranger.
//
// BUILT FROM THE ERP'S IDENTIFIER, SO IT IS THE SAME ON EVERY RUN. A random one would change each time the import
// ran, and each change would look like a real update to somebody reading the report.
//
// WITH A SHORT HASH OF THAT IDENTIFIER ON THE END, because turning "A & B Trading" and "A-B Trading" into
// something an address can hold makes them the same text, and two suppliers cannot share one account.
//
// IT NEVER REPLACES A REAL ADDRESS. That rule is enforced where the import updates a supplier, and the flag on the
// result is what lets it: a supplier who later gave the portal a real email must not have it overwritten by a
// placeholder because the ERP still has none.
//
//
// THE OTHER GAPS
//
// A CURRENCY THE PORTAL DOES NOT KNOW IS LEFT EMPTY, not defaulted to SYP. Defaulting would silently reprice the
// supplier and the ministry's feed would report that price as fact; empty says "not known", which is true.
//
// A MISSING NAME FALLS BACK TO THE ERP'S IDENTIFIER, which in this ERP is itself the supplier's name as first
// entered, so it is a reasonable name rather than a code.
//
// A SUPPLIER DISABLED IN THE ERP ARRIVES SUSPENDED: visible in the registry, excluded from invitations, and
// reversible, which is what "disabled" means there. Deactivated would have been the wrong word - in this product
// that state is permanent.

namespace MotsSupplierPortal.Application.Integration;

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

public sealed record AdmittedSupplier(
    string Name,
    string Email,
    bool EmailIsPlaceholder,
    string? Currency,
    bool Suspended,
    IReadOnlyList<string> Notes);

public static partial class ErpImportAdmission
{
    public const string PlaceholderDomain = "erp-import.invalid";

    public static readonly IReadOnlyList<string> KnownCurrencies = ["SYP", "USD"];

    public static AdmittedSupplier Admit(ErpSupplier supplier)
    {
        var notes = new List<string>();

        var name = supplier.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = supplier.ExternalId;
            notes.Add("No name in the ERP; its identifier is used as the name.");
        }

        var email = supplier.Email?.Trim().ToLowerInvariant();
        var placeholder = email is null;
        if (placeholder)
        {
            email = PlaceholderEmail(supplier.ExternalId);
            notes.Add(
                $"No email in the ERP; placeholder {email} assigned. It cannot receive mail - replace it once the "
                + "real contact is known.");
        }

        var currency = supplier.Currency;
        if (currency is not null && !KnownCurrencies.Contains(currency, StringComparer.OrdinalIgnoreCase))
        {
            notes.Add(
                $"The currency '{currency}' is not one the portal knows ({string.Join(", ", KnownCurrencies)}); "
                + "left empty rather than guessed.");
            currency = null;
        }

        if (supplier.Disabled)
        {
            notes.Add("Disabled in the ERP; arrives suspended - visible, but cannot be invited to tenders.");
        }

        return new AdmittedSupplier(name!, email!, placeholder, currency?.ToUpperInvariant(), supplier.Disabled, notes);
    }

    public static bool IsPlaceholder(string? email) =>
        email is not null && email.EndsWith("@" + PlaceholderDomain, StringComparison.OrdinalIgnoreCase);

    public static string PlaceholderEmail(string externalId)
    {
        var slug = NotAddressable().Replace(externalId.ToLowerInvariant(), "-").Trim('-');
        if (slug.Length > 40) slug = slug[..40].TrimEnd('-');
        if (slug.Length == 0) slug = "supplier";

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(externalId)))[..6].ToLowerInvariant();

        return $"{slug}-{hash}@{PlaceholderDomain}";
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotAddressable();
}
