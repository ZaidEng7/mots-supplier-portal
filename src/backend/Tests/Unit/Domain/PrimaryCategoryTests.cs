// Exactly one category is the primary one, for as long as a supplier has any.
//
// WHY THE INVARIANT MATTERS OUTSIDE THIS PRODUCT. The ministry's dashboard reads one SupplierGroup per
// supplier and a supplier here may claim up to fifty categories, so something has to answer "which one".
// Choosing in the export would mean inventing a rule - there is no ranking on a category link - and the rule
// would be arbitrary, would differ between the export and the screen, and would change under a supplier's
// feet when they added a category. The supplier answers instead, and this file is the guarantee that there
// is always exactly one answer to read.
//
// THE THREE PATHS THAT CAN BREAK IT are linking, unlinking and setting, and each is tested on both sides:
// the first link becomes primary AND the second does not; removing the primary promotes another AND removing
// a non-primary leaves the primary alone; setting one moves it AND clears the old, which is the half that a
// "set the flag" implementation forgets and which the database's filtered unique index would then refuse.
//
// REMOVING THE LAST CATEGORY LEAVES NO PRIMARY, and that is correct rather than a gap: a supplier who
// supplies nothing has no main thing. The assertion is that it does not throw reaching for a promotion
// candidate that is not there.
//
// A CODE THAT IS NOT LINKED IS REFUSED rather than silently linked-and-promoted. Setting a primary is a
// choice among what a supplier already claims; accepting an unknown code here would let a supplier acquire a
// category through the wrong door, skipping the check that it exists and is active.

namespace MotsSupplierPortal.Tests.Unit.Domain;

using FluentAssertions;
using MotsSupplierPortal.Domain.Suppliers;
using Xunit;

public sealed class PrimaryCategoryTests
{
    private static Supplier Editable()
    {
        var supplier = Supplier.Register(
            $"SUP-2026-{Random.Shared.Next(1, 999_999):D6}", "شركة", "Test Co", "CR-1", "Owner", "owner@example.com");
        supplier.MarkEmailVerified();
        return supplier;
    }

    [Fact]
    public void The_first_category_linked_becomes_the_primary_one()
    {
        var supplier = Editable();

        supplier.LinkCategory("catering", isComplianceCritical: false);

        supplier.PrimaryCategoryCode.Should().Be("catering");
    }

    [Fact]
    public void A_later_category_does_not_take_the_primary_from_the_first()
    {
        var supplier = Editable();

        supplier.LinkCategory("catering", isComplianceCritical: false);
        supplier.LinkCategory("transport", isComplianceCritical: false);

        supplier.PrimaryCategoryCode.Should().Be(
            "catering",
            "a supplier adding a second thing they supply has not said it is now their main one");
        supplier.CategoryLinks.Count(l => l.IsPrimary).Should().Be(1);
    }

    [Fact]
    public void Setting_a_primary_moves_it_and_clears_the_previous_one()
    {
        var supplier = Editable();
        supplier.LinkCategory("catering", isComplianceCritical: false);
        supplier.LinkCategory("transport", isComplianceCritical: false);

        supplier.SetPrimaryCategory("transport", isComplianceCritical: false);

        supplier.PrimaryCategoryCode.Should().Be("transport");
        supplier.CategoryLinks.Count(l => l.IsPrimary).Should().Be(
            1,
            "leaving the old flag set is what the database's filtered unique index refuses, and it is the half "
            + "a 'set the flag' implementation forgets");
    }

    [Fact]
    public void Setting_a_category_the_supplier_does_not_claim_is_refused()
    {
        var supplier = Editable();
        supplier.LinkCategory("catering", isComplianceCritical: false);

        var act = () => supplier.SetPrimaryCategory("transport", isComplianceCritical: false);

        act.Should().Throw<DomainException>().WithMessage("*not linked*");
        supplier.PrimaryCategoryCode.Should().Be("catering");
    }

    [Fact]
    public void Unlinking_the_primary_promotes_one_of_the_others()
    {
        var supplier = Editable();
        supplier.LinkCategory("catering", isComplianceCritical: false);
        supplier.LinkCategory("transport", isComplianceCritical: false);

        supplier.UnlinkCategory("catering", isComplianceCritical: false);

        supplier.PrimaryCategoryCode.Should().Be("transport");
        supplier.CategoryLinks.Count(l => l.IsPrimary).Should().Be(1);
    }

    [Fact]
    public void Unlinking_a_category_that_is_not_the_primary_leaves_the_primary_alone()
    {
        var supplier = Editable();
        supplier.LinkCategory("catering", isComplianceCritical: false);
        supplier.LinkCategory("transport", isComplianceCritical: false);

        supplier.UnlinkCategory("transport", isComplianceCritical: false);

        supplier.PrimaryCategoryCode.Should().Be("catering");
    }

    [Fact]
    public void Unlinking_the_last_category_leaves_no_primary_and_does_not_throw()
    {
        var supplier = Editable();
        supplier.LinkCategory("catering", isComplianceCritical: false);

        var act = () => supplier.UnlinkCategory("catering", isComplianceCritical: false);

        act.Should().NotThrow();
        supplier.PrimaryCategoryCode.Should().BeNull("a supplier who supplies nothing has no main thing");
        supplier.CategoryLinks.Should().BeEmpty();
    }
}
