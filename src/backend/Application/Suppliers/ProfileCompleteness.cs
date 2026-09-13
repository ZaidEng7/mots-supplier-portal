// How much of the registration checklist a supplier has finished, as a fraction.
//
//
// THE DEFINITION IS THE SPECIFICATION'S
//
// The written requirement names it directly: a completeness evaluator over the required sections and the
// mandatory document types, computed on the server so the interface cannot bypass it.
//
// Both halves already exist as tested rules elsewhere: the supplier's own missing-fields list for the
// sections, and the document evaluator for the types.
//
//
// WHY THIS RATIO AND NOT ANOTHER
//
// It is exactly the set the submit gate enforces, because submission is refused on the union of those same two
// lists.
//
// So a supplier at the top of the meter can submit, and one below it is looking at precisely what is stopping
// them. A meter measuring anything else would be worse than no meter, because it would tell a supplier they
// were ready while the server disagreed.
//
// An earlier version measured required documents alone and omitted the profile fields entirely, so a supplier
// with every document and no legal information read as complete and could not submit.
//
//
// THE INSTABILITY IT DOES NOT FIX
//
// Both halves move when reference data moves. Activating a new required document type lowers every supplier's
// completeness without any of them doing anything.
//
// That is the rule rather than a defect, because a ministry adding a mandatory certificate genuinely does make
// previously complete profiles incomplete. Including the fixed profile fields only softens it by enlarging the
// denominator. Said plainly rather than claimed away.
//
//
// TWO EDGE CASES
//
// A supplier with nothing required of them is complete rather than empty. No requirements is a finished
// checklist, and a zero would read as somebody who has done nothing.
//
// The result is rounded to two places, matching the figure the written contract shows.

namespace MotsSupplierPortal.Application.Suppliers;

public static class ProfileCompleteness
{
    public static double Ratio(int missingItems, int totalItems)
    {
        if (totalItems <= 0) return 1;

        var satisfied = Math.Max(0, totalItems - missingItems);
        return Math.Round((double)satisfied / totalItems, 2);
    }
}
