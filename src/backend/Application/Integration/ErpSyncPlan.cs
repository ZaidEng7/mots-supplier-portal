// Deciding, before anything is written, which portal suppliers a run will re-link, hold back or suspend.
//
// THIS IS ONE PLACE BECAUSE THE PREVIEW AND THE RUN MUST AGREE, and they did not. The first version worked out the
// suspension limit inside the run, after that night's new suppliers had been created - each new supplier raised the
// limit the same read was judged against, so the preview could say "held back" and the run then suspend. A forecast
// that disagrees with the outcome is worse than none, because it was believed. Both now call this, on data read
// before the first write.
//
//
// RENAMES
//
// ERPNext lets a supplier be renamed, and the rename changes its identifier - the only key this import matches on.
// Without this, a rename looked exactly like a deletion followed by a new supplier: the portal suspended the real
// one, with its tender history and documents, and then either refused the "new" one because its email was taken or
// created a duplicate on a placeholder.
//
// THE SAME REAL EMAIL MEANS THE SAME SUPPLIER, and the portal re-links it. Logins are unique by email in this
// product, so two ERP identifiers carrying one real address are one company. Placeholders never count: they are
// built from the identifier, so a renamed supplier's placeholder is by construction a different string.
//
// A MATCHING TAX NUMBER ALONE IS NOT ENOUGH TO ACT ON. A company and its subsidiary can share one, and re-linking the
// wrong pair would move a supplier's history onto a stranger. So that pair is held: the new one is not created, the
// old one is not suspended, and the report says how to resolve it - ask Seven Gates to put the same email on both,
// and the next run links them itself.
//
// AN AMBIGUOUS MATCH IS NO MATCH. If one new identifier could be two vanished suppliers, or two new identifiers
// claim one vanished supplier, nothing is paired and the ordinary rules apply. Guessing between them is how history
// ends up on the wrong company.
//
//
// SUSPENSIONS
//
// THE LIMIT IS JUDGED AGAINST ACTIVE SUPPLIERS, because only active ones can be suspended. Counting every linked
// supplier - including the ones already suspended - made "a quarter" mean more each night, as each night's
// suspensions grew the number the next night was measured against.
//
// A SUPPLIER ALREADY SUSPENDED FOR BEING GONE, AND SINCE REINSTATED BY A PERSON, IS LEFT ALONE. Otherwise the next
// night would suspend it again, and the night after that, and a person's decision would be undone forever by a job
// nobody watches. It becomes a candidate again only after it reappears in the ERP and disappears a second time.

namespace MotsSupplierPortal.Application.Integration;

public sealed record PortalLinkedSupplier(
    string ExternalId,
    string ReferenceCode,
    string? Name,
    string? TaxId,
    string? Email,
    bool IsActive,
    bool AlreadyRemovedFromErp);

public enum ErpRenameKind
{
    SameEmail,
    SameTaxNumberOnly,
}

public sealed record ErpRename(string OldExternalId, string NewExternalId, string ReferenceCode, ErpRenameKind Kind);

public sealed record ErpSyncPlan(
    IReadOnlyList<ErpRename> Relinks,
    IReadOnlyList<ErpRename> HeldRenames,
    IReadOnlyList<PortalLinkedSupplier> ToSuspend,
    int ActiveLinked,
    string? SuspensionsHeldBack)
{
    public static ErpSyncPlan Build(IReadOnlyList<ErpSupplier> erp, IReadOnlyList<PortalLinkedSupplier> portal)
    {
        var inPortal = portal.Select(p => p.ExternalId).ToHashSet(StringComparer.Ordinal);
        var inErp = erp.Select(e => e.ExternalId).ToHashSet(StringComparer.Ordinal);

        var vanished = portal.Where(p => !inErp.Contains(p.ExternalId)).ToList();
        var arrivals = erp.Where(e => !inPortal.Contains(e.ExternalId)).OrderBy(e => e.ExternalId, StringComparer.Ordinal).ToList();

        var relinks = Pair(arrivals, vanished, ByEmail, ErpRenameKind.SameEmail);

        var pairedOld = relinks.Select(r => r.OldExternalId).ToHashSet(StringComparer.Ordinal);
        var pairedNew = relinks.Select(r => r.NewExternalId).ToHashSet(StringComparer.Ordinal);

        var held = Pair(
            [.. arrivals.Where(a => !pairedNew.Contains(a.ExternalId))],
            [.. vanished.Where(v => !pairedOld.Contains(v.ExternalId))],
            ByTaxNumber,
            ErpRenameKind.SameTaxNumberOnly);

        var renamedOld = pairedOld.Concat(held.Select(r => r.OldExternalId)).ToHashSet(StringComparer.Ordinal);

        var missing = vanished
            .Where(v => v.IsActive && !v.AlreadyRemovedFromErp && !renamedOld.Contains(v.ExternalId))
            .OrderBy(v => v.ReferenceCode, StringComparer.Ordinal)
            .ToList();

        var activeLinked = portal.Count(p => p.IsActive);
        var decision = ErpMissingSupplierPolicy.Decide(erp.Count, activeLinked, missing.Count);

        return new ErpSyncPlan(
            relinks,
            held,
            decision.MaySuspend ? missing : [],
            activeLinked,
            decision.HeldBackBecause);
    }

    private static bool ByEmail(ErpSupplier arrival, PortalLinkedSupplier vanished)
    {
        var email = arrival.Email?.Trim().ToLowerInvariant();

        return email is not null
            && !ErpImportAdmission.IsPlaceholder(email)
            && vanished.Email is not null
            && !ErpImportAdmission.IsPlaceholder(vanished.Email)
            && string.Equals(email, vanished.Email.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool ByTaxNumber(ErpSupplier arrival, PortalLinkedSupplier vanished) =>
        !string.IsNullOrWhiteSpace(arrival.TaxId)
        && !string.IsNullOrWhiteSpace(vanished.TaxId)
        && string.Equals(arrival.TaxId.Trim(), vanished.TaxId.Trim(), StringComparison.Ordinal);

    // A pair is made only when the match is unique in BOTH directions: this arrival matches exactly one vanished
    // supplier, and that supplier is matched by exactly one arrival.
    private static List<ErpRename> Pair(
        IReadOnlyList<ErpSupplier> arrivals,
        IReadOnlyList<PortalLinkedSupplier> vanished,
        Func<ErpSupplier, PortalLinkedSupplier, bool> matches,
        ErpRenameKind kind)
    {
        var pairs = new List<ErpRename>();

        foreach (var arrival in arrivals)
        {
            var candidates = vanished.Where(v => matches(arrival, v)).ToList();
            if (candidates.Count != 1) continue;

            var only = candidates[0];
            if (arrivals.Count(a => matches(a, only)) != 1) continue;

            pairs.Add(new ErpRename(only.ExternalId, arrival.ExternalId, only.ReferenceCode, kind));
        }

        return pairs;
    }
}
