// Deciding who is re-linked, held or suspended, before anything is written.
//
// EACH TEST HERE IS A FINDING FROM THE REVIEW OF THE FIRST VERSION, which got every one of them wrong:
//
//   the limit was judged against every linked supplier, including already-suspended ones, so "a quarter" grew with
//   each night's suspensions;
//   a supplier a person reinstated was suspended again the next night, and every night after;
//   a supplier renamed in the ERP was suspended as deleted and replaced by a stranger or a refusal.
//
// THE RENAME TESTS PIN BOTH SIDES OF A JUDGEMENT CALL. A shared real email re-links, because logins are unique by
// email. A shared tax number alone only holds, because a company and its subsidiary can share one - and moving a
// supplier's history onto the wrong company is worse than asking a person. Placeholders never match, because they are
// built from the identifier and so differ by construction.
//
// AMBIGUITY PAIRS NOTHING. One arrival that could be either of two vanished suppliers must not be guessed at.

namespace MotsSupplierPortal.Tests.Unit.Erp;

using FluentAssertions;
using MotsSupplierPortal.Application.Integration;

public sealed class ErpSyncPlanTests
{
    private static ErpSupplier Erp(string id, string? email = null, string? taxId = null) =>
        new(id, id, "Local", "Company", taxId, "Syria", email, null, false, "SYP", null, null,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private static PortalLinkedSupplier Portal(
        string id, string? email = null, string? taxId = null, bool active = true, bool removed = false) =>
        new(id, "REF-" + id, id, taxId, email, active, removed);

    [Fact]
    public void A_supplier_renamed_in_the_erp_with_the_same_real_email_is_relinked_not_suspended()
    {
        var plan = ErpSyncPlan.Build(
            [Erp("Al-Sham Trading LLC", email: "sales@alsham.example")],
            [Portal("Al Sham Trading", email: "sales@alsham.example")]);

        plan.Relinks.Should().ContainSingle()
            .Which.Should().Be(new ErpRename("Al Sham Trading", "Al-Sham Trading LLC", "REF-Al Sham Trading", ErpRenameKind.SameEmail));
        plan.ToSuspend.Should().BeEmpty("a rename is not a deletion - the supplier's history must survive it");
    }

    [Fact]
    public void A_placeholder_email_never_makes_two_suppliers_one()
    {
        var placeholder = ErpImportAdmission.PlaceholderEmail("Old Name");

        var plan = ErpSyncPlan.Build([Erp("New Name", email: placeholder)], [Portal("Old Name", email: placeholder)]);

        plan.Relinks.Should().BeEmpty("placeholders are not anybody's real address");
    }

    [Fact]
    public void A_matching_tax_number_alone_holds_both_rather_than_guessing()
    {
        var plan = ErpSyncPlan.Build(
            [Erp("Homs Linen Group", taxId: "0200-4455")],
            [Portal("Homs Linen Mills", taxId: "0200-4455")]);

        plan.Relinks.Should().BeEmpty("a company and its subsidiary can share a tax number");
        plan.HeldRenames.Should().ContainSingle().Which.Kind.Should().Be(ErpRenameKind.SameTaxNumberOnly);
        plan.ToSuspend.Should().BeEmpty("the old record must not be suspended while a person decides");
    }

    [Fact]
    public void An_arrival_that_could_be_either_of_two_vanished_suppliers_is_not_paired()
    {
        var plan = ErpSyncPlan.Build(
            [Erp("Merged Co", taxId: "T-1")],
            [Portal("Old A", taxId: "T-1"), Portal("Old B", taxId: "T-1")]);

        plan.Relinks.Should().BeEmpty();
        plan.HeldRenames.Should().BeEmpty("guessing between two companies is how history ends up on the wrong one");
    }

    [Fact]
    public void A_supplier_already_suspended_as_removed_and_since_reinstated_is_not_suspended_again()
    {
        var plan = ErpSyncPlan.Build(
            [Erp("Stays")],
            [Portal("Stays"), Portal("Reinstated By A Person", removed: true)]);

        plan.ToSuspend.Should().BeEmpty(
            "a person reinstated it; suspending it again every night would undo their decision forever");
    }

    [Fact]
    public void The_limit_is_a_quarter_of_ACTIVE_suppliers_not_of_every_linked_one()
    {
        var portal = Enumerable.Range(0, 50).Select(i => Portal($"active-{i}"))
            .Concat(Enumerable.Range(0, 30).Select(i => Portal($"suspended-{i}", active: false)))
            .ToList();

        var erp = Enumerable.Range(20, 30).Select(i => Erp($"active-{i}")).ToList();

        var plan = ErpSyncPlan.Build(erp, portal);

        plan.ActiveLinked.Should().Be(50);
        plan.ToSuspend.Should().BeEmpty(
            "20 of 50 active suppliers is 40%; counting the 30 already suspended made it look like a quarter");
        plan.SuspensionsHeldBack.Should().Contain("12");
    }

    [Fact]
    public void Ordinary_deletions_are_still_suspended()
    {
        var plan = ErpSyncPlan.Build(
            [Erp("A"), Erp("B"), Erp("C")],
            [Portal("A"), Portal("B"), Portal("C"), Portal("Gone")]);

        plan.ToSuspend.Should().ContainSingle().Which.ExternalId.Should().Be("Gone");
    }
}
