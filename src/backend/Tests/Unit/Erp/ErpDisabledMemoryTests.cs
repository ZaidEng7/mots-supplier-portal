// What a supplier remembers about the ERP disabling it.
//
// THE THREE MEMORIES MIRROR THE ONES FOR A SUPPLIER MISSING FROM THE ERP, and each test here is a way the earlier
// versions got it wrong. The first suspended on every night the ERP said "disabled", undoing a person's reinstatement
// forever. The second suspended only on the change from enabled to disabled, so a disable that arrived while the
// supplier was already suspended left no trace: a document approval then reactivated it, and nothing ever suspended it
// for the disable. So the sequences below walk a supplier through reactivations as well as reads.
//
// THE CONTROL IS THE PLAIN CASE, an active supplier being disabled and suspended, because every other test is about
// what must NOT happen and would pass against a method that never suspended anybody.

namespace MotsSupplierPortal.Tests.Unit.Erp;

using FluentAssertions;
using MotsSupplierPortal.Domain.Suppliers;

public sealed class ErpDisabledMemoryTests
{
    private static Supplier Imported(bool disabled = false) =>
        Supplier.ImportFromErp(
            "SUP-2026-000001", "Homs Linen Mills", "Homs Linen Mills", null, SupplierLegalType.Company, "SYP",
            "Homs Linen Mills", "sales@homslinen.example", null, disabled);

    [Fact]
    public void An_active_supplier_the_erp_disables_is_suspended()
    {
        var supplier = Imported();

        supplier.RecordErpDisabled(true).Should().Be(ErpDisabledChange.Suspended);
        supplier.LifecycleState.Should().Be(SupplierLifecycleState.Suspended);
        supplier.ErpDisabledState.Should().Be(SupplierErpDisabledState.SuspendedAsDisabled);
    }

    [Fact]
    public void A_supplier_imported_disabled_counts_as_suspended_for_it()
    {
        var supplier = Imported(disabled: true);
        supplier.Reactivate("The ministry still works with them directly.");

        supplier.RecordErpDisabled(true).Should().Be(ErpDisabledChange.None);
        supplier.LifecycleState.Should().Be(
            SupplierLifecycleState.Active,
            "it arrived suspended for the disable, so a person's reinstatement stands");
    }

    [Fact]
    public void A_person_who_reinstates_a_supplier_suspended_for_a_disable_is_not_overruled()
    {
        var supplier = Imported();
        supplier.RecordErpDisabled(true);
        supplier.Reactivate("The ministry still works with them directly.");

        supplier.RecordErpDisabled(true).Should().Be(ErpDisabledChange.None);
        supplier.LifecycleState.Should().Be(SupplierLifecycleState.Active);
    }

    [Fact]
    public void A_disable_that_arrives_while_suspended_is_marked_and_holds_the_supplier_back()
    {
        var supplier = Imported();
        supplier.Suspend("A licence expired.");

        supplier.RecordErpDisabled(true).Should().Be(ErpDisabledChange.Marked);
        supplier.LifecycleState.Should().Be(SupplierLifecycleState.Suspended);
        supplier.IsMarkedAsUnwantedByErp.Should().BeTrue(
            "the second version kept no trace of this disable, so a document approval brought the supplier back");
    }

    [Fact]
    public void A_marked_supplier_found_active_is_suspended_once_and_a_second_reinstatement_stands()
    {
        var supplier = Imported();
        supplier.Suspend("A licence expired.");
        supplier.RecordErpDisabled(true);
        supplier.Reactivate("The matter is closed.");

        supplier.RecordErpDisabled(true).Should().Be(
            ErpDisabledChange.Suspended,
            "the second version only suspended on the change to disabled, which had already passed, so it never did");
        supplier.LifecycleState.Should().Be(SupplierLifecycleState.Suspended);

        supplier.Reactivate("Still works with us directly.");

        supplier.RecordErpDisabled(true).Should().Be(ErpDisabledChange.None);
        supplier.LifecycleState.Should().Be(SupplierLifecycleState.Active);
    }

    [Fact]
    public void Re_enabling_clears_a_mark_and_says_so_but_never_reinstates()
    {
        var marked = Imported();
        marked.Suspend("A licence expired.");
        marked.RecordErpDisabled(true);

        marked.RecordErpDisabled(false).Should().Be(ErpDisabledChange.Cleared);
        marked.LifecycleState.Should().Be(SupplierLifecycleState.Suspended);
        marked.IsMarkedAsUnwantedByErp.Should().BeFalse();

        var suspended = Imported();
        suspended.RecordErpDisabled(true);

        suspended.RecordErpDisabled(false).Should().Be(
            ErpDisabledChange.None,
            "the sync suspended it, and reinstating somebody the ERP turned away is a person's decision");
        suspended.LifecycleState.Should().Be(SupplierLifecycleState.Suspended);
    }

    [Fact]
    public void A_later_disable_after_re_enabling_counts_as_new()
    {
        var supplier = Imported();
        supplier.RecordErpDisabled(true);
        supplier.Reactivate("The ministry still works with them directly.");
        supplier.RecordErpDisabled(false);

        supplier.RecordErpDisabled(true).Should().Be(ErpDisabledChange.Suspended);
    }
}
