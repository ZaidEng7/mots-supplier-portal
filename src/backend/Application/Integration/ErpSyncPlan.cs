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
// ONLY A SUPPLIER THAT VANISHED TONIGHT CAN BE THE OLD SIDE: one not yet recorded as gone from the ERP, WHATEVER its
// lifecycle. A supplier marked months ago must not block a genuinely new company that shares its tax number forever -
// but a supplier a person SUSPENDED is still the same company, and when the second version limited the old side to
// active suppliers, a suspended company renamed in the ERP came straight back as a brand-new active one, undoing the
// suspension with a green badge. So every vanished supplier is marked the first night it goes: active ones are
// suspended as they are marked, inactive ones are only marked.
//
// A DISABLED ARRIVAL SHIELDS NOTHING. If the ERP has disabled the record, there is nothing to protect: if it is the
// same company, the ERP has switched it off; if it is a different one, the vanished supplier really is gone. Either way
// the old supplier must not stay invitable, so both sides follow the ordinary rules. The check comes after the
// ambiguity checks, so a disabled arrival still counts when deciding whether a match is unique.
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
//
// A SUPPLIER ONLY MARKED AS GONE, AND SINCE BACK IN SERVICE, IS SUSPENDED ONCE. It was already suspended or
// deactivated when it left, so the run only marked it and never suspended it for its absence. The fourth review found
// the mark had been sharing the reinstated memory above, so a supplier reactivated afterwards stayed active and
// invitable although the ERP no longer had it. The automatic reinstatement after a document renewal no longer
// reactivates such a supplier at all (see ApproveDocumentHandler), so what is left is a person reactivating it.
//
// THE SYNC SUSPENDS EVERY SUPPLIER ONCE FOR AN ABSENCE, and a person's reinstatement after that stands. That holds
// here too, deliberately, including for a supplier the person had suspended to confirm a held-back clear-out: a mark
// cannot tell that confirmation from a suspension for some unrelated cause, and a person reactivating a supplier they
// suspended for another reason may not know it has left the ERP - the suspension, and the audit row naming the reason,
// is how they find out. Reinstating it a second time is respected. It is not a rename candidate: it left on an earlier
// night, and the same months-later reasoning applies. It counts against the same limit as tonight's.
//
// A DISABLE IS REMEMBERED THE SAME WAY, on the supplier rather than here, because a disabled supplier is still in
// the list and so never reaches this plan - see Supplier.RecordErpDisabled.
//
// A READ THAT IS NOT BELIEVED MARKS NOBODY, and an empty read is never believed even when only suppliers already out
// of service are missing. Why the quarter limit applies to suspensions only is in ErpMissingSupplierPolicy.

namespace MotsSupplierPortal.Application.Integration;

public sealed record PortalLinkedSupplier(
    string ExternalId,
    string ReferenceCode,
    string? Name,
    string? TaxId,
    string? LoginEmail,
    bool IsActive,
    bool SuspendedAsRemovedFromErp,
    bool MarkedRemovedFromErp = false)
{
    public bool RecordedAsGone => SuspendedAsRemovedFromErp || MarkedRemovedFromErp;
}

public sealed record ErpProbableRename(string OldExternalId, string NewExternalId, string ReferenceCode, string Signal);

public sealed record ErpSyncPlan(
    IReadOnlyList<ErpProbableRename> ProbableRenames,
    IReadOnlyList<PortalLinkedSupplier> ToSuspend,
    IReadOnlyList<PortalLinkedSupplier> ToMarkRemoved,
    int ActiveLinked,
    string? SuspensionsHeldBack)
{
    public static ErpSyncPlan Build(IReadOnlyList<ErpSupplier> erp, IReadOnlyList<PortalLinkedSupplier> portal)
    {
        var inPortal = portal.Select(p => p.ExternalId).ToHashSet(StringComparer.Ordinal);
        var inErp = erp.Select(e => e.ExternalId).ToHashSet(StringComparer.Ordinal);

        var vanishedTonight = portal
            .Where(p => !inErp.Contains(p.ExternalId) && !p.RecordedAsGone)
            .ToList();

        var backInServiceWhileGone = portal
            .Where(p => !inErp.Contains(p.ExternalId) && p.MarkedRemovedFromErp && p.IsActive)
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
        var activeMissing = missing
            .Where(m => m.IsActive)
            .Concat(backInServiceWhileGone)
            .OrderBy(m => m.ReferenceCode, StringComparer.Ordinal)
            .ToList();

        var outOfServiceMissing = missing.Where(m => !m.IsActive).ToList();

        var decision = ErpMissingSupplierPolicy.Decide(
            erp.Count, activeLinked, activeMissing.Count, outOfServiceMissing.Count);

        return new ErpSyncPlan(
            renames,
            decision.MaySuspend ? activeMissing : [],
            decision.MaySuspend ? outOfServiceMissing : [],
            activeLinked,
            decision.HeldBackBecause);
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

            if (arrival.Disabled) continue;

            pairs.Add(new ErpProbableRename(only.ExternalId, arrival.ExternalId, only.ReferenceCode, Signal(arrival, only)!));
        }

        return pairs;
    }
}
