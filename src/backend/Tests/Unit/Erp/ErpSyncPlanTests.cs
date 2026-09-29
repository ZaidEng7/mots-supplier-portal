// Deciding who is held for a person or suspended, before anything is written.
//
// EVERY TEST HERE CAME OUT OF A REVIEW. The first version judged the suspension limit against every linked supplier,
// re-suspended suppliers people had reinstated, and treated an ERP rename as a deletion. The second version fixed the
// rename by re-linking automatically, and the next review showed three ways that moved one company's history onto
// another: a purchasing email two ERP suppliers share, a contact email the supplier can edit, and a tax number shared
// by a company and its subsidiary.
//
// A THIRD REVIEW FOUND TWO MORE: a disabled arrival shielded the old supplier and kept it invitable, and limiting
// the old side to active suppliers let a suspended company come back as a new active one when it was renamed.
//
// A FOURTH FOUND THE MARK FOR A SUPPLIER ALREADY OUT OF SERVICE sharing the "a person reinstated it" memory, so one
// reactivated later stayed active although the ERP no longer had it; and an empty read marking every such supplier.
//
// SO THE RENAME TESTS PIN A REFUSAL TO GUESS. A probable rename holds both sides - the vanished supplier is not
// suspended, the arrival is not created - and nothing is ever moved. The tests check the signals (sign-in address or
// tax number), that placeholders never count, that ambiguity pairs nothing, and that only a supplier which vanished
// IN THIS RUN can be the old side, so one removed months ago cannot block a new company forever.

namespace MotsSupplierPortal.Tests.Unit.Erp;

using FluentAssertions;
using MotsSupplierPortal.Application.Integration;

public sealed class ErpSyncPlanTests
{
    private static ErpSupplier Erp(string id, string? email = null, string? taxId = null, bool disabled = false) =>
        new(id, id, "Local", "Company", taxId, "Syria", email, null, disabled, "SYP", null, null,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private static PortalLinkedSupplier Portal(
        string id,
        string? login = null,
        string? taxId = null,
        bool active = true,
        bool suspendedAsRemoved = false,
        bool marked = false) =>
        new(id, "REF-" + id, id, taxId, login, active, suspendedAsRemoved, marked);

    [Fact]
    public void A_probable_rename_by_sign_in_address_is_held_and_nothing_is_moved_or_suspended()
    {
        var plan = ErpSyncPlan.Build(
            [Erp("Al-Sham Trading LLC", email: "sales@alsham.example")],
            [Portal("Al Sham Trading", login: "sales@alsham.example")]);

        plan.ProbableRenames.Should().ContainSingle()
            .Which.Should().Be(new ErpProbableRename(
                "Al Sham Trading", "Al-Sham Trading LLC", "REF-Al Sham Trading", "the same sign-in address"));
        plan.ToSuspend.Should().BeEmpty("the existing supplier carries on while a person checks");
    }

    [Fact]
    public void A_probable_rename_by_tax_number_is_held_the_same_way()
    {
        var plan = ErpSyncPlan.Build(
            [Erp("Homs Linen Group", taxId: "0200-4455")],
            [Portal("Homs Linen Mills", taxId: "0200-4455")]);

        plan.ProbableRenames.Should().ContainSingle().Which.Signal.Should().Be("the same tax number");
        plan.ToSuspend.Should().BeEmpty();
    }

    [Fact]
    public void A_placeholder_address_never_makes_two_suppliers_look_like_one()
    {
        var placeholder = ErpImportAdmission.PlaceholderEmail("Old Name");

        ErpSyncPlan.Build([Erp("New Name", email: placeholder)], [Portal("Old Name", login: placeholder)])
            .ProbableRenames.Should().BeEmpty();
    }

    [Fact]
    public void An_arrival_that_could_be_either_of_two_vanished_suppliers_is_not_paired()
    {
        ErpSyncPlan.Build([Erp("Merged Co", taxId: "T-1")], [Portal("Old A", taxId: "T-1"), Portal("Old B", taxId: "T-1")])
            .ProbableRenames.Should().BeEmpty("guessing between two companies is how history ends up on the wrong one");
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, false, true)]
    public void A_supplier_already_recorded_as_gone_cannot_hold_a_new_company(
        bool active, bool suspendedAsRemoved, bool marked)
    {
        var plan = ErpSyncPlan.Build(
            [Erp("New Subsidiary", taxId: "0200-4455")],
            [Portal(
                "Parent Removed Months Ago",
                taxId: "0200-4455",
                active: active,
                suspendedAsRemoved: suspendedAsRemoved,
                marked: marked)]);

        plan.ProbableRenames.Should().BeEmpty(
            "a supplier marked gone long ago would otherwise block a genuinely new company that shares its tax number, "
            + "on every run, forever");
    }

    [Fact]
    public void A_suspended_supplier_renamed_in_the_erp_is_held_rather_than_recreated_as_a_new_active_one()
    {
        var plan = ErpSyncPlan.Build(
            [Erp("Homs Linen Group", taxId: "0200-4455")],
            [Portal("Homs Linen Mills", taxId: "0200-4455", active: false)]);

        plan.ProbableRenames.Should().ContainSingle(
            "the second version held only active suppliers, so a company a person suspended came back as a new active "
            + "supplier the moment Seven Gates renamed it");
    }

    [Fact]
    public void A_supplier_already_out_of_service_that_leaves_the_erp_is_marked_not_suspended()
    {
        var plan = ErpSyncPlan.Build([Erp("Stays")], [Portal("Stays"), Portal("Suspended Earlier", active: false)]);

        plan.ToSuspend.Should().BeEmpty("it is suspended already");
        plan.ToMarkRemoved.Should().ContainSingle().Which.ExternalId.Should().Be("Suspended Earlier");
    }

    [Fact]
    public void A_disabled_arrival_shields_nothing()
    {
        var plan = ErpSyncPlan.Build(
            [Erp("Al Sham Trading (Closed)", email: "sales@alsham.example", disabled: true)],
            [Portal("Al Sham Trading", login: "sales@alsham.example")]);

        plan.ProbableRenames.Should().BeEmpty(
            "the ERP has switched the record off; holding it would keep the old supplier invitable indefinitely");
        plan.ToSuspend.Should().ContainSingle().Which.ExternalId.Should().Be("Al Sham Trading");
    }

    [Fact]
    public void A_disabled_arrival_still_counts_when_deciding_whether_a_match_is_ambiguous()
    {
        var plan = ErpSyncPlan.Build(
            [Erp("Renamed", taxId: "T-9"), Erp("Renamed And Closed", taxId: "T-9", disabled: true)],
            [Portal("Original", taxId: "T-9")]);

        plan.ProbableRenames.Should().BeEmpty("two arrivals claim one supplier, so nobody is paired");
        plan.ToSuspend.Should().ContainSingle();
    }

    [Fact]
    public void A_supplier_already_suspended_as_removed_and_since_reinstated_is_not_suspended_again()
    {
        ErpSyncPlan.Build([Erp("Stays")], [Portal("Stays"), Portal("Reinstated By A Person", suspendedAsRemoved: true)])
            .ToSuspend.Should().BeEmpty("a person reinstated it; suspending it on every run would undo that forever");
    }

    [Fact]
    public void A_supplier_only_marked_as_gone_and_since_back_in_service_is_suspended_and_holds_nobody()
    {
        var plan = ErpSyncPlan.Build(
            [Erp("Stays"), Erp("Shares Its Tax Number", taxId: "0200-4455")],
            [Portal("Stays"), Portal("Reactivated After It Left", taxId: "0200-4455", marked: true)]);

        plan.ToSuspend.Should().ContainSingle(
                "it was out of service when it left, so the sync never suspended it for its absence; sharing the "
                + "reinstated memory kept it active and invitable once it was reactivated")
            .Which.ExternalId.Should().Be("Reactivated After It Left");
        plan.ProbableRenames.Should().BeEmpty("it left on an earlier run, so it is not this run's rename");
    }

    [Fact]
    public void A_supplier_only_marked_as_gone_and_still_out_of_service_is_left_alone()
    {
        var plan = ErpSyncPlan.Build([Erp("Stays")], [Portal("Stays"), Portal("Still Suspended", active: false, marked: true)]);

        plan.ToSuspend.Should().BeEmpty();
        plan.ToMarkRemoved.Should().BeEmpty("it is marked already");
    }

    [Fact]
    public void An_empty_read_marks_nobody_even_when_nothing_active_is_missing()
    {
        var plan = ErpSyncPlan.Build([], [Portal("Suspended A", active: false), Portal("Suspended B", active: false)]);

        plan.ToMarkRemoved.Should().BeEmpty(
            "the first version checked only active suppliers, so an empty read marked every suspended one as gone");
        plan.SuspensionsHeldBack.Should().Contain("returned no suppliers");
    }

    [Fact]
    public void The_limit_is_a_quarter_of_ACTIVE_suppliers_not_of_every_linked_one()
    {
        var portal = Enumerable.Range(0, 50).Select(i => Portal($"active-{i}"))
            .Concat(Enumerable.Range(0, 30).Select(i => Portal($"suspended-{i}", active: false)))
            .ToList();

        var plan = ErpSyncPlan.Build([.. Enumerable.Range(20, 30).Select(i => Erp($"active-{i}"))], portal);

        plan.ActiveLinked.Should().Be(50);
        plan.ToSuspend.Should().BeEmpty("20 of 50 active is 40%; counting the 30 already suspended hid that");
        plan.ToMarkRemoved.Should().BeEmpty("a read that is not believed marks nobody either");
        plan.SuspensionsHeldBack.Should().Contain("12");
    }

    [Fact]
    public void Ordinary_deletions_are_still_suspended()
    {
        ErpSyncPlan.Build([Erp("A"), Erp("B"), Erp("C")], [Portal("A"), Portal("B"), Portal("C"), Portal("Gone")])
            .ToSuspend.Should().ContainSingle().Which.ExternalId.Should().Be("Gone");
    }
}
