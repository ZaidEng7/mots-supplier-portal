// Deciding, before anything is written, which portal suppliers a run will hold for a person or suspend.
//
// THIS IS ONE PLACE BECAUSE THE PREVIEW AND THE RUN MUST AGREE, and they did not. The first version worked out the
// suspension limit inside the run, after that night's new suppliers had been created - each new supplier raised the
// limit the same read was judged against, so the preview could say "held back" and the run then suspend. A forecast
// that disagrees with the outcome is worse than none, because it was believed. Both now call this, on data read
// before the first write.
//
//
// PROBABLE RENAMES ARE DETECTED ONLY TO PROTECT, NEVER TO RE-LINK
//
// ERPNext can rename a supplier, and a rename changes the identifier this import matches on. Left alone, a rename
// looks like a deletion plus a stranger: the real supplier is suspended and the "new" one is refused because its
// email is taken, or duplicated on a placeholder.
//
// THE SECOND VERSION RE-LINKED AUTOMATICALLY, and a review showed three ways it moved one company's history onto
// another: two ERP suppliers sharing a purchasing email (the ERP does not enforce unique addresses), a contact email
// the supplier can edit themselves, and a tax number shared by a company and its subsidiary. Each wrong guess was
// silent and each moved awards, documents and a login onto a different legal entity, with an audit entry claiming a
// rename that never happened. There is no signal in this data strong enough to justify that, so nothing is moved.
//
// INSTEAD, A PROBABLE RENAME HOLDS BOTH SIDES: the supplier that vanished is not suspended, the arrival is not created,
// and the run is flagged for a person. Nothing is lost while they look - the existing supplier keeps working exactly
// as before - and a wrong guess costs one supplier waiting rather than one company wearing another's history.
//
// THE SIGNALS ARE THE SIGN-IN ADDRESS AND THE TAX NUMBER. The sign-in address, not the representative's contact email,
// because the contact is editable by the supplier and the login is unique in this product. Placeholders never count:
// they are built from the identifier, so they differ by construction.
//
// ONLY A SUPPLIER THAT VANISHED TONIGHT CAN BE THE OLD SIDE: one that is active and not already marked as removed. A
// supplier removed months ago must not block a genuinely new company that happens to share its tax number forever.
//
// AN AMBIGUOUS MATCH IS NO MATCH. If an arrival could be either of two vanished suppliers, or two arrivals claim one,
// nothing is paired and the ordinary rules apply.
//
//
// SUSPENSIONS
//
// THE LIMIT IS JUDGED AGAINST ACTIVE SUPPLIERS, because only active ones can be suspended. Counting every linked
// supplier made "a quarter" grow with each night's suspensions.
//
// A SUPPLIER ALREADY SUSPENDED FOR BEING GONE, AND SINCE REINSTATED BY A PERSON, IS LEFT ALONE. Otherwise the next
// night would suspend it again, forever. It becomes a candidate again only after it reappears in the ERP and
// disappears a second time.

namespace MotsSupplierPortal.Application.Integration;

public sealed record PortalLinkedSupplier(
    string ExternalId,
    string ReferenceCode,
    string? Name,
    string? TaxId,
    string? LoginEmail,
    bool IsActive,
    bool AlreadyRemovedFromErp);

public sealed record ErpProbableRename(string OldExternalId, string NewExternalId, string ReferenceCode, string Signal);

public sealed record ErpSyncPlan(
    IReadOnlyList<ErpProbableRename> ProbableRenames,
    IReadOnlyList<PortalLinkedSupplier> ToSuspend,
    int ActiveLinked,
    string? SuspensionsHeldBack)
{
    public static ErpSyncPlan Build(IReadOnlyList<ErpSupplier> erp, IReadOnlyList<PortalLinkedSupplier> portal)
    {
        var inPortal = portal.Select(p => p.ExternalId).ToHashSet(StringComparer.Ordinal);
        var inErp = erp.Select(e => e.ExternalId).ToHashSet(StringComparer.Ordinal);

        var vanishedTonight = portal
            .Where(p => !inErp.Contains(p.ExternalId) && p.IsActive && !p.AlreadyRemovedFromErp)
            .ToList();

        var arrivals = erp
            .Where(e => !inPortal.Contains(e.ExternalId))
            .OrderBy(e => e.ExternalId, StringComparer.Ordinal)
            .ToList();

        var renames = Pair(arrivals, vanishedTonight);
        var heldOld = renames.Select(r => r.OldExternalId).ToHashSet(StringComparer.Ordinal);

        var missing = vanishedTonight
            .Where(v => !heldOld.Contains(v.ExternalId))
            .OrderBy(v => v.ReferenceCode, StringComparer.Ordinal)
            .ToList();

        var activeLinked = portal.Count(p => p.IsActive);
        var decision = ErpMissingSupplierPolicy.Decide(erp.Count, activeLinked, missing.Count);

        return new ErpSyncPlan(renames, decision.MaySuspend ? missing : [], activeLinked, decision.HeldBackBecause);
    }

    private static string? Signal(ErpSupplier arrival, PortalLinkedSupplier vanished)
    {
        var email = arrival.Email?.Trim().ToLowerInvariant();

        if (email is not null
            && !ErpImportAdmission.IsPlaceholder(email)
            && vanished.LoginEmail is not null
            && !ErpImportAdmission.IsPlaceholder(vanished.LoginEmail)
            && string.Equals(email, vanished.LoginEmail.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return "the same sign-in address";
        }

        if (!string.IsNullOrWhiteSpace(arrival.TaxId)
            && !string.IsNullOrWhiteSpace(vanished.TaxId)
            && string.Equals(arrival.TaxId.Trim(), vanished.TaxId.Trim(), StringComparison.Ordinal))
        {
            return "the same tax number";
        }

        return null;
    }

    // A pair is made only when the match is unique in BOTH directions: this arrival matches exactly one vanished
    // supplier, and that supplier is matched by exactly one arrival.
    private static List<ErpProbableRename> Pair(
        IReadOnlyList<ErpSupplier> arrivals, IReadOnlyList<PortalLinkedSupplier> vanished)
    {
        var pairs = new List<ErpProbableRename>();

        foreach (var arrival in arrivals)
        {
            var candidates = vanished.Where(v => Signal(arrival, v) is not null).ToList();
            if (candidates.Count != 1) continue;

            var only = candidates[0];
            if (arrivals.Count(a => Signal(a, only) is not null) != 1) continue;

            pairs.Add(new ErpProbableRename(only.ExternalId, arrival.ExternalId, only.ReferenceCode, Signal(arrival, only)!));
        }

        return pairs;
    }
}
