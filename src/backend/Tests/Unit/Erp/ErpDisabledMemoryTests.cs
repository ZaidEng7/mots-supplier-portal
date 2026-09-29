// What a supplier remembers about the ERP disabling it.
//
// THE THREE MEMORIES MIRROR THE ONES FOR A SUPPLIER MISSING FROM THE ERP, and each test here is a way the earlier
// versions got it wrong. The first suspended on every run on which the ERP said "disabled", undoing a person's reinstatement
// forever. The second suspended only on the change from enabled to disabled, so a disable that arrived while the
// supplier was already suspended left no trace: a document approval then reactivated it, and nothing ever suspended it
// for the disable. So the sequences below walk a supplier through reactivations as well as reads.
//
// THE CONTROL IS THE PLAIN CASE, an active supplier being disabled and suspended, because every other test is about
// what must NOT happen and would pass against a method that never suspended anybody.

namespace MotsSupplierPortal.Tests.Unit.Erp;

using FluentAssertions;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Suppliers;

public sealed class ErpDisabledMemoryTests
{
    private static Supplier Imported(bool disabled = false) =>
        Supplier.ImportFromErp(
            "SUP-2026-000001", "Homs Linen Mills", "Homs Linen Mills", null, SupplierLegalType.Company, "SYP",
            "Homs Linen Mills", "sales@homslinen.example", null, disabled ? ErpStanding.Disabled : ErpStanding.Usable);

    [Fact]
    public void An_active_supplier_the_erp_disables_is_suspended()
    {
        var supplier = Imported();

        supplier.RecordErpStanding(ErpStanding.Disabled).Should().Be(ErpDisabledChange.Suspended);
        supplier.LifecycleState.Should().Be(SupplierLifecycleState.Suspended);
        supplier.ErpDisabledState.Should().Be(SupplierErpDisabledState.SuspendedAsDisabled);
    }

    [Fact]
    public void A_supplier_imported_disabled_counts_as_suspended_for_it()
    {
        var supplier = Imported(disabled: true);
        supplier.Reactivate("The ministry still works with them directly.");

        supplier.RecordErpStanding(ErpStanding.Disabled).Should().Be(ErpDisabledChange.None);
        supplier.LifecycleState.Should().Be(
            SupplierLifecycleState.Active,
            "it arrived suspended for the disable, so a person's reinstatement stands");
    }

    [Fact]
    public void A_person_who_reinstates_a_supplier_suspended_for_a_disable_is_not_overruled()
    {
        var supplier = Imported();
        supplier.RecordErpStanding(ErpStanding.Disabled);
        supplier.Reactivate("The ministry still works with them directly.");

        supplier.RecordErpStanding(ErpStanding.Disabled).Should().Be(ErpDisabledChange.None);
        supplier.LifecycleState.Should().Be(SupplierLifecycleState.Active);
    }

    [Fact]
    public void A_disable_that_arrives_while_suspended_is_marked_and_holds_the_supplier_back()
    {
        var supplier = Imported();
        supplier.Suspend("A licence expired.");

        supplier.RecordErpStanding(ErpStanding.Disabled).Should().Be(ErpDisabledChange.Marked);
        supplier.LifecycleState.Should().Be(SupplierLifecycleState.Suspended);
        supplier.IsMarkedAsUnwantedByErp.Should().BeTrue(
            "the second version kept no trace of this disable, so a document approval brought the supplier back");
    }

    [Fact]
    public void A_marked_supplier_found_active_is_suspended_once_and_a_second_reinstatement_stands()
    {
        var supplier = Imported();
        supplier.Suspend("A licence expired.");
        supplier.RecordErpStanding(ErpStanding.Disabled);
        supplier.Reactivate("The matter is closed.");

        supplier.RecordErpStanding(ErpStanding.Disabled).Should().Be(
            ErpDisabledChange.Suspended,
            "the second version only suspended on the change to disabled, which had already passed, so it never did");
        supplier.LifecycleState.Should().Be(SupplierLifecycleState.Suspended);

        supplier.Reactivate("Still works with us directly.");

        supplier.RecordErpStanding(ErpStanding.Disabled).Should().Be(ErpDisabledChange.None);
        supplier.LifecycleState.Should().Be(SupplierLifecycleState.Active);
    }

    [Fact]
    public void Re_enabling_clears_a_mark_and_says_so_but_never_reinstates()
    {
        var marked = Imported();
        marked.Suspend("A licence expired.");
        marked.RecordErpStanding(ErpStanding.Disabled);

        marked.RecordErpStanding(ErpStanding.Usable).Should().Be(ErpDisabledChange.Cleared);
        marked.LifecycleState.Should().Be(SupplierLifecycleState.Suspended);
        marked.IsMarkedAsUnwantedByErp.Should().BeFalse();

        var suspended = Imported();
        suspended.RecordErpStanding(ErpStanding.Disabled);

        suspended.RecordErpStanding(ErpStanding.Usable).Should().Be(
            ErpDisabledChange.None,
            "the sync suspended it, and reinstating somebody the ERP turned away is a person's decision");
        suspended.LifecycleState.Should().Be(SupplierLifecycleState.Suspended);
    }

    [Fact]
    public void A_later_disable_after_re_enabling_counts_as_new()
    {
        var supplier = Imported();
        supplier.RecordErpStanding(ErpStanding.Disabled);
        supplier.Reactivate("The ministry still works with them directly.");
        supplier.RecordErpStanding(ErpStanding.Usable);

        supplier.RecordErpStanding(ErpStanding.Disabled).Should().Be(ErpDisabledChange.Suspended);
    }

    [Fact]
    public void A_supplier_the_sync_suspended_only_while_the_erp_approved_it_comes_back_when_the_erp_does()
    {
        var supplier = Imported();

        supplier.RecordErpStanding(ErpStanding.AwaitingApproval).Should().Be(ErpDisabledChange.Suspended);
        supplier.ErpDisabledState.Should().Be(SupplierErpDisabledState.SuspendedAsPending);

        supplier.RecordErpStanding(ErpStanding.Usable).Should().Be(
            ErpDisabledChange.Released,
            "otherwise every new supplier the hourly sync meets while it waits for Seven Gates stays suspended by hand");
        supplier.LifecycleState.Should().Be(SupplierLifecycleState.Active);
        supplier.ErpDisabledState.Should().Be(SupplierErpDisabledState.NotDisabled);
    }

    [Fact]
    public void A_new_supplier_that_arrives_waiting_for_approval_comes_into_service_when_approved()
    {
        var supplier = Supplier.ImportFromErp(
            "SUP-2026-000002", "Damascus Bakeries", "Damascus Bakeries", null, SupplierLegalType.Company, "SYP",
            "Damascus Bakeries", "orders@dambakeries.example", null, ErpStanding.AwaitingApproval);

        supplier.LifecycleState.Should().Be(SupplierLifecycleState.Suspended);
        supplier.RecordErpStanding(ErpStanding.Usable).Should().Be(ErpDisabledChange.Released);
        supplier.LifecycleState.Should().Be(SupplierLifecycleState.Active);
    }

    [Fact]
    public void A_pending_suspension_a_person_has_acted_on_is_never_lifted_by_the_erp()
    {
        var supplier = Imported();
        supplier.RecordErpStanding(ErpStanding.AwaitingApproval);
        supplier.Reactivate("The ministry needs them for an urgent tender.");
        supplier.Suspend("Suspended by the ministry for cause.");

        supplier.RecordErpStanding(ErpStanding.Usable).Should().Be(
            ErpDisabledChange.None,
            "the suspension is a person's now; the ERP approving the record says nothing about their reason");
        supplier.LifecycleState.Should().Be(SupplierLifecycleState.Suspended);
    }

    [Fact]
    public void A_person_who_reinstates_a_pending_supplier_is_not_overruled_while_it_is_still_pending()
    {
        var supplier = Imported();
        supplier.RecordErpStanding(ErpStanding.AwaitingApproval);
        supplier.Reactivate("The ministry needs them for an urgent tender.");

        supplier.RecordErpStanding(ErpStanding.AwaitingApproval).Should().Be(ErpDisabledChange.None);
        supplier.LifecycleState.Should().Be(SupplierLifecycleState.Active);
    }

    [Fact]
    public void A_pending_supplier_the_erp_then_disables_is_not_released_if_it_is_later_enabled_again()
    {
        var supplier = Imported();
        supplier.RecordErpStanding(ErpStanding.AwaitingApproval);
        supplier.RecordErpStanding(ErpStanding.Disabled).Should().Be(ErpDisabledChange.None);

        supplier.RecordErpStanding(ErpStanding.Usable).Should().Be(
            ErpDisabledChange.None, "the ERP may lift only its own wait; a disable is lifted by a person here");
        supplier.LifecycleState.Should().Be(SupplierLifecycleState.Suspended);
    }

    [Fact]
    public void A_person_keeps_a_pending_suspension_by_suspending_the_supplier_again()
    {
        var supplier = Imported();
        supplier.RecordErpStanding(ErpStanding.AwaitingApproval);

        supplier.Suspend("Sanctions check pending.");

        supplier.LifecycleState.Should().Be(SupplierLifecycleState.Suspended);
        supplier.ErpDisabledState.Should().Be(
            SupplierErpDisabledState.SuspendedAsDisabled,
            "it already showed as suspended, so without this a person had no way to keep it out but a reinstatement "
            + "nobody meant, or a deactivation that cannot be undone");
        supplier.RecordErpStanding(ErpStanding.Usable).Should().Be(ErpDisabledChange.None);
        supplier.LifecycleState.Should().Be(SupplierLifecycleState.Suspended);
    }

    [Fact]
    public void A_supplier_that_is_not_waiting_for_the_erp_still_cannot_be_suspended_twice()
    {
        var supplier = Imported();
        supplier.Suspend("Suspended by the ministry for cause.");

        var again = () => supplier.Suspend("Again.");

        again.Should().Throw<DomainException>("only the sync's pending hold may be taken over this way");
    }

    [Theory]
    [InlineData(SupplierErpDisabledState.NotDisabled, true, ErpStanding.AwaitingApproval,
        ErpDisabledChange.Suspended, SupplierLifecycleState.Suspended, SupplierErpDisabledState.SuspendedAsPending)]
    [InlineData(SupplierErpDisabledState.NotDisabled, true, ErpStanding.Disabled,
        ErpDisabledChange.Suspended, SupplierLifecycleState.Suspended, SupplierErpDisabledState.SuspendedAsDisabled)]
    [InlineData(SupplierErpDisabledState.SuspendedAsPending, false, ErpStanding.Usable,
        ErpDisabledChange.Released, SupplierLifecycleState.Active, SupplierErpDisabledState.NotDisabled)]
    [InlineData(SupplierErpDisabledState.SuspendedAsPending, false, ErpStanding.Disabled,
        ErpDisabledChange.None, SupplierLifecycleState.Suspended, SupplierErpDisabledState.SuspendedAsDisabled)]
    [InlineData(SupplierErpDisabledState.MarkedDisabled, false, ErpStanding.Usable,
        ErpDisabledChange.Cleared, SupplierLifecycleState.Suspended, SupplierErpDisabledState.NotDisabled)]
    [InlineData(SupplierErpDisabledState.NotDisabled, false, ErpStanding.Disabled,
        ErpDisabledChange.Marked, SupplierLifecycleState.Suspended, SupplierErpDisabledState.MarkedDisabled)]
    public void Recording_does_what_the_preview_forecasts_and_leaves_the_supplier_as_stated(
        SupplierErpDisabledState state,
        bool active,
        ErpStanding standing,
        ErpDisabledChange expected,
        SupplierLifecycleState lifecycleAfter,
        SupplierErpDisabledState stateAfter)
    {
        var supplier = Imported();
        if (state == SupplierErpDisabledState.SuspendedAsPending) supplier.RecordErpStanding(ErpStanding.AwaitingApproval);
        if (!active && state != SupplierErpDisabledState.SuspendedAsPending) supplier.Suspend("Out of service for an unrelated reason.");
        if (state == SupplierErpDisabledState.MarkedDisabled) supplier.RecordErpStanding(ErpStanding.Disabled);

        Supplier.ErpDisabledChangeFor(supplier.ErpDisabledState, supplier.LifecycleState == SupplierLifecycleState.Active, standing)
            .Should().Be(expected, "this is what the preview shows before the hourly run acts");

        supplier.RecordErpStanding(standing).Should().Be(expected);
        supplier.LifecycleState.Should().Be(lifecycleAfter);
        supplier.ErpDisabledState.Should().Be(stateAfter);
    }
}
