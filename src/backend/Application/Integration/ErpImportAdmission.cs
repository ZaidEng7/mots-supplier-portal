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
//
// A SUPPLIER THE ERP HAS NOT APPROVED ARRIVES SUSPENDED TOO, for the same reason and with its state in the note.
// Seven Gates runs an approval workflow, and a record still waiting for the chief accountant is not one the ministry
// should invite to a tender. Only a supplier with no workflow state at all, or one marked Approved, arrives active.
//
//
// WHAT IS LEFT EMPTY
//
// ANYTHING THE ERP DOES NOT HAVE STAYS EMPTY, rather than being filled with something that looks like data. Two
// fields cannot be empty, because the portal requires them: a new supplier's Arabic name starts as the English one,
// and its representative is named after the company. An existing supplier keeps what it has. The notes are worded to
// be true of both, because the same admission serves a create and an update.
//
// A VALUE LONGER THAN ITS COLUMN IS MEASURED HERE, not discovered by the database refusing the supplier; the rule is
// in ErpFieldLimits.
//
// THE ADDRESS IS NOT DECIDED HERE, only mapped. Whether it is written depends on the supplier the portal already has
// - one with an address keeps it, one under review cannot be edited - so AddressOutcome decides that, for the preview
// and the run alike.

namespace MotsSupplierPortal.Application.Integration;

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MotsSupplierPortal.Domain.Suppliers;

public sealed record AdmittedSupplier(
    string Name,
    string Email,
    bool EmailIsPlaceholder,
    string? Currency,
    bool Suspended,
    IReadOnlyList<string> Notes,
    string? ArabicName = null,
    string? ContactPerson = null,
    ErpAddressMapping? Address = null,
    string? Description = null,
    string? SupplierGroup = null,
    string? RegistrationType = null,
    string? TurnedAway = null,
    string? ArrivalNote = null,
    ErpStanding Standing = ErpStanding.Usable)
{
    public string RepresentativeName => ContactPerson ?? Name;
}

public sealed record ErpAddressOutcome(bool Write, string Note);

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

        var turnedAway = TurnedAwayReason(supplier);
        var arrivalNote = supplier.Disabled
            ? "Disabled in the ERP; arrives suspended - visible, but cannot be invited to tenders."
            : turnedAway is null
                ? null
                : $"Not yet approved in the ERP ('{supplier.WorkflowState?.Trim()}'); arrives suspended - visible, but "
                  + "cannot be invited to tenders - and comes into service by itself once the ERP approves it, unless "
                  + "a person acts on it first.";

        var arabicName = string.IsNullOrWhiteSpace(supplier.ArabicName) ? null : supplier.ArabicName.Trim();
        if (arabicName is null)
        {
            notes.Add("No Arabic name in the ERP; a supplier the portal already has keeps its own, and a new one "
                      + "starts with the English name.");
        }

        var person = string.IsNullOrWhiteSpace(supplier.ContactPersonName) ? null : supplier.ContactPersonName.Trim();
        if (person is null)
        {
            notes.Add("No contact person in the ERP; an existing representative keeps their name, and a new one is "
                      + "named after the company.");
        }

        return new AdmittedSupplier(
            name!,
            email!,
            placeholder,
            currency?.ToUpperInvariant(),
            turnedAway is not null,
            notes,
            ErpFieldLimits.Cut(arabicName, ErpFieldLimits.Name, "Arabic name", notes),
            ErpFieldLimits.Cut(person, ErpFieldLimits.PersonName, "contact person's name", notes),
            ErpAddressMapper.Map(supplier.Address),
            ErpFieldLimits.Cut(supplier.Description, ErpFieldLimits.Description, "description", notes),
            ErpFieldLimits.DropIfTooLong(supplier.SupplierGroup, ErpFieldLimits.SupplierGroup, "supplier group", notes),
            ErpFieldLimits.DropIfTooLong(
                supplier.RegistrationType, ErpFieldLimits.RegistrationType, "registration type", notes),
            turnedAway,
            arrivalNote,
            StandingOf(supplier));
    }

    // Whether the ERP lets this supplier be used, is still approving it, or has disabled it. Disabled wins when both
    // are true: a disabled record is not waiting for anything, and the sync never lifts a disable by itself.
    public static ErpStanding StandingOf(ErpSupplier supplier)
    {
        if (supplier.Disabled) return ErpStanding.Disabled;

        var workflowState = supplier.WorkflowState?.Trim();
        var notApproved = !string.IsNullOrEmpty(workflowState)
            && !string.Equals(workflowState, ApprovedWorkflowState, StringComparison.OrdinalIgnoreCase);

        return notApproved ? ErpStanding.AwaitingApproval : ErpStanding.Usable;
    }

    // Why the ERP is turning this supplier away - disabled, or not approved - or null if it is not.
    //
    // ONE RULE FOR EVERY PLACE THAT ASKS. A new supplier arrives suspended on it, an existing one is suspended once on
    // it, and a probable rename is not held for it. Three copies would drift, and the first one already had: renames
    // looked only at "disabled" after "not approved" had been added everywhere else.
    public static string? TurnedAwayReason(ErpSupplier supplier) => StandingOf(supplier) switch
    {
        ErpStanding.Disabled => "Disabled in the ERP",
        ErpStanding.AwaitingApproval => $"Not approved in the ERP ('{supplier.WorkflowState?.Trim()}')",
        _ => null,
    };

    public const string ReleasedNote =
        "Approved in the ERP now. It was suspended here only while it waited for that, and nobody has changed it since, "
        + "so it is back in service.";

    // What an update row says when the ERP turned the supplier away but this run held the suspension back.
    public static string HeldBackNote(string reason) =>
        $"{reason}; not suspended in this run, because too many suppliers were turned away at once - see the summary.";

    // What an update row says about a supplier the ERP is turning away, for the preview and the run alike.
    //
    // THE NOTE FOLLOWS WHAT WAS DONE. A new supplier "arrives suspended"; one the portal already has is suspended only
    // once, so on later runs the same ERP state does nothing, and a note still saying "suspended" would report a
    // suspension that did not happen - to a person reading a run nobody watched.
    public static string TurnedAwayNote(string reason, ErpDisabledChange change) => change switch
    {
        ErpDisabledChange.Suspended => $"{reason}; suspended. A person who reinstates it will not be overruled.",
        ErpDisabledChange.Marked =>
            $"{reason} while already out of service here; marked, so it will not come back into service automatically.",
        _ => $"{reason}; already dealt with on an earlier run, so left as it is.",
    };

    // What becomes of the ERP's address for one supplier, and the note that says so.
    //
    // A NEW SUPPLIER TAKES IT. One the portal already has takes it only if it has no address yet and its details may be
    // edited: an existing address may have been corrected here, and a supplier whose application is waiting for review
    // - or was refused - cannot have its contact details changed underneath the reviewer. Either way the note says what
    // happened, so an update row never claims an address was imported when the portal kept its own.
    public static ErpAddressOutcome AddressOutcome(
        AdmittedSupplier admitted, bool isNew, int addressesInPortal, string? blockedByState)
    {
        var mapping = admitted.Address ?? ErpAddressMapper.Map(null);

        if (mapping.Address is null) return new ErpAddressOutcome(false, mapping.Note);
        if (isNew) return new ErpAddressOutcome(true, mapping.Note);

        if (addressesInPortal > 0)
        {
            return new ErpAddressOutcome(false, "The supplier already has an address in the portal; the ERP's was not applied.");
        }

        if (blockedByState is not null)
        {
            return new ErpAddressOutcome(
                false,
                $"The ERP's address was not added: the supplier is {blockedByState}, and its details cannot be changed "
                + "until that is settled. The next run adds it.");
        }

        return new ErpAddressOutcome(true, mapping.Note);
    }

    public const string ApprovedWorkflowState = "Approved";

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
