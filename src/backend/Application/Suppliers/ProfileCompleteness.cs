namespace MotsSupplierPortal.Application.Suppliers;

/// <summary>
/// §12.2's <c>profileCompleteness</c>: how much of the onboarding checklist a supplier has done.
///
/// <para><b>The definition is the specification's, not an invention.</b> BACKLOG.md's
/// <c>T-03.1.1b</c> names it directly - <i>"Server-side completeness evaluator (required sections +
/// mandatory doc types satisfied)"</i> - and FEAT-03.1's Definition of Done adds <i>"completeness
/// computed server-side (UI cannot bypass)"</i>. Both halves already exist as tested predicates:
/// <c>Supplier.GetMissingProfileFields()</c> for the sections and
/// <c>DocumentCompletenessEvaluator</c> for the document types.</para>
///
/// <para><b>Why this ratio and not a different one.</b> It is exactly the set the SUBMIT GATE
/// enforces - <c>Supplier.Submit</c> refuses on the union of those same two lists. So a supplier at
/// 100% can submit, and one below it is looking at precisely what is stopping them. A meter that
/// measured anything else would be worse than no meter, because it would tell a supplier they were
/// ready when the server disagreed.</para>
///
/// <para><b>What EPIC-16 computed, and why it is not adopted.</b> The supplier dashboard measured
/// required documents supplied ÷ required documents total, omitting the six profile fields
/// entirely. A supplier with every document and no legal information read as 100% complete and
/// could not submit. That is the narrower of the two numbers and the spec names the wider one.</para>
///
/// <para><b>The instability the wider definition does not fix.</b> Both halves move when reference
/// data moves: activating a new required DocumentType lowers every supplier's completeness without
/// any of them doing anything. That is the rule rather than a defect - a ministry adding a mandatory
/// certificate genuinely makes previously-complete profiles incomplete - and including the six
/// fixed profile fields only softens it by enlarging the denominator. Said plainly rather than
/// claimed away.</para>
/// </summary>
public static class ProfileCompleteness
{
    /// <param name="missingItems">The union of missing profile fields and missing required document
    /// type codes - the same list the submit gate refuses on.</param>
    /// <param name="totalItems">Every checklist item, satisfied or not.</param>
    /// <returns>
    /// A fraction in [0, 1], rounded to two places to match §12.2's own <c>0.62</c>.
    ///
    /// <para>A supplier with nothing required of them is 1, not 0 - "no requirements" is a complete
    /// profile, and a zero would read as a supplier who has done nothing.</para>
    /// </returns>
    public static double Ratio(int missingItems, int totalItems)
    {
        if (totalItems <= 0) return 1;

        var satisfied = Math.Max(0, totalItems - missingItems);
        return Math.Round((double)satisfied / totalItems, 2);
    }
}
