// Whether this run may suspend the suppliers that have disappeared from the ERP.
//
// WHY THIS EXISTS AT ALL. Seven Gates deletes a supplier by removing it, and a removed supplier simply stops
// appearing in what the ERP returns. So "missing from the list" is the only signal a deletion leaves - and it is
// exactly the signal a broken read leaves too. A credential narrowed overnight, a permission removed, a server that
// answers politely with an empty page: every one of those returns fewer suppliers than exist, and a job that
// believed the list would suspend the ministry's whole supplier base in a run nobody was watching, with nobody to
// notice until tenders could not be sent.
//
// SO THE LIST IS BELIEVED ONLY WHEN IT LOOKS LIKE A NORMAL DAY. An empty list is never believed: an ERP with
// suppliers in it does not lose all of them between one run and the next. A list missing a large share of the
// suppliers the portal knows is not believed either, and the run says so instead of acting. Real deletions arrive a
// few at a time; a cliff is a fault.
//
// THE QUARTER IS OF ACTIVE SUPPLIERS, TAKEN BEFORE THE RUN WRITES ANYTHING. Only active suppliers can be suspended,
// and counting the already-suspended ones made the limit grow with every run's suspensions. The count is taken by
// the caller before the import creates that run's new suppliers, because each new one would otherwise raise the
// limit the same read is being judged against - see ErpSyncPlan.
//
// THE THRESHOLD IS A QUARTER, WITH A FLOOR OF FIVE. A quarter because Seven Gates has about eighty suppliers and a
// genuine clear-out of twenty at once is already unusual enough to deserve a person's eyes. The floor because
// with a handful of suppliers a quarter rounds to one, and a single legitimate deletion would then be refused
// forever. Both numbers are stated in the message the run produces, so whoever reads it knows what was held back and
// why.
//
// THE EMPTY LIST ALSO STOPS THE MARKING of suppliers already out of service. The run does not suspend those, it only
// marks them as gone, and the first version let the marking through whenever nothing active was missing - so on a
// portal whose ERP suppliers were all suspended, an empty read marked every one of them.
//
// THE QUARTER IS NOT APPLIED TO THEM, deliberately. When a real clear-out is held back, the way a person confirms it
// is to suspend those suppliers here; the next run then finds them out of service and only marks them. A limit on
// marks would hold that confirmation back too, on every run, with nothing left a person could do to clear it. If that
// person later reinstates one of them, the next run suspends it once more, as ErpSyncPlan explains.
//
// WHAT A WRONG MARK COSTS, since a narrowed read under the limit can make one. A mark changes no lifecycle itself,
// and the next run that finds the supplier in the ERP clears it. While it stands, two things wait: the automatic
// reinstatement after a document renewal, which the sync asks again the moment the mark clears (see
// AutomaticReinstatement), and the protection against a rename, which cannot be given back - a supplier the ERP
// renamed in the very window a bad read had hidden it is treated as new, as it would be months later. That needs a
// narrowed read and a rename of the same supplier together, and a limit on marks would cost more than it saves.
//
// HELD BACK IS NOT FAILED. The rest of the import still runs - new suppliers are created, changed ones updated - and
// only the suspensions wait. Refusing the whole run would stop the useful work to protect against a danger that only
// the suspensions carry.

namespace MotsSupplierPortal.Application.Integration;

public sealed record ErpMissingSupplierDecision(bool MaySuspend, string? HeldBackBecause);

public static class ErpMissingSupplierPolicy
{
    public const double LargestShareSuspendedInOneRun = 0.25;
    public const int AlwaysAllowed = 5;

    public static ErpMissingSupplierDecision Decide(
        int suppliersInErp,
        int activeLinkedInPortal,
        int missingFromErp,
        int outOfServiceMissingFromErp = 0)
    {
        if (missingFromErp + outOfServiceMissingFromErp == 0)
        {
            return new ErpMissingSupplierDecision(true, null);
        }

        if (suppliersInErp == 0)
        {
            return new ErpMissingSupplierDecision(
                false,
                $"The ERP returned no suppliers at all, so the {missingFromErp + outOfServiceMissingFromErp} the "
                + "portal holds from it were left as they were. An ERP does not lose every supplier overnight; check "
                + "the connection and its permissions.");
        }

        var limit = Math.Max(AlwaysAllowed, (int)Math.Floor(activeLinkedInPortal * LargestShareSuspendedInOneRun));

        if (missingFromErp > limit)
        {
            return new ErpMissingSupplierDecision(
                false,
                $"{missingFromErp} suppliers have disappeared from the ERP, more than the {limit} one run may "
                + $"suspend (a quarter of the {activeLinkedInPortal} active suppliers imported from it, and never fewer "
                + $"than {AlwaysAllowed}). "
                + "None were suspended. If Seven Gates really removed them, a person should confirm it first.");
        }

        return new ErpMissingSupplierDecision(true, null);
    }

    // Whether this run may suspend the active suppliers the ERP now turns away - disables, or has not approved.
    //
    // THE SAME QUARTER, FOR THE SAME REASON. A handful of suppliers turned away is Seven Gates' ordinary work; most of
    // them at once is a change on their side - a workflow renamed, re-saved, or reset to its first state - and an hourly
    // run nobody watches would otherwise suspend the ministry's whole supplier base and leave a person to reinstate
    // every one by hand, even after the ERP is put right. Above the limit nothing is suspended and the run says so.
    public static string? TurnedAwayHeldBack(int activeLinkedInPortal, int turnedAway)
    {
        var limit = Math.Max(AlwaysAllowed, (int)Math.Floor(activeLinkedInPortal * LargestShareSuspendedInOneRun));

        return turnedAway <= limit
            ? null
            : $"The ERP turned away {turnedAway} active suppliers at once - disabled, or not approved - more than the "
              + $"{limit} one run may suspend (a quarter of the {activeLinkedInPortal} active suppliers imported from it, "
              + $"and never fewer than {AlwaysAllowed}). None were suspended for it. Check with Seven Gates whether "
              + "something changed on their side before anyone suspends them.";
    }
}
