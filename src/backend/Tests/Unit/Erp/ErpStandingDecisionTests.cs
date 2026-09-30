// What an import does about the ERP's standing for a supplier the portal already holds, as the preview reports it and
// the run applies it.
//
// ONE TEST PER CHANGE, because each is a different promise on the report a person reads: released, waiting for its
// documents, release withdrawn, suspended, marked, cleared, left alone. Which change applies is ErpDisabledChangeFor's,
// and ErpDisabledMemoryTests walks a real supplier through those; these pin what the decision adds on top - the note a
// row carries, the plan's hold, and the two things the run does afterwards.
//
// THE HELD-BACK TESTS ARE THE ONES NOTHING ELSE PINS CHEAPLY. Reaching a hold through the preview or the run needs
// more suppliers turned away at once than one run may suspend; here it is one flag. They check that a hold records
// nothing, that its note tells a change held back from one an earlier run already made, that it never holds back a
// supplier the ERP lets be used, and that it keeps a mark as gone.
//
// THE REASONS COME FROM ErpImportAdmission, as they do in the preview and the run, so a test cannot pass on a reason
// that no ERP record would produce.
//
// THE CONTROL IS A SUPPLIER THE ERP LETS BE USED AND THE SYNC NEVER ACTED ON: no change and no note, because every
// other test here is about a note and would pass against a decision that noted everything.

namespace MotsSupplierPortal.Tests.Unit.Erp;

using FluentAssertions;
using MotsSupplierPortal.Application.Integration;
using MotsSupplierPortal.Domain.Suppliers;

public sealed class ErpStandingDecisionTests
{
    private static ErpSupplier Erp(bool disabled = false, string? workflowState = null) =>
        ErpSupplierTestFactory.Supplier("Homs Linen Mills") with
        {
            Email = "sales@homslinen.example",
            Disabled = disabled,
            WorkflowState = workflowState,
        };

    private static ErpStandingDecision Decide(
        ErpSupplier erp,
        SupplierErpDisabledState memory = SupplierErpDisabledState.NotDisabled,
        bool active = true,
        bool documentsAllowRelease = true,
        bool planHolds = false,
        bool markedGone = false)
    {
        var admitted = ErpImportAdmission.Admit(erp);

        return ErpStandingDecision.Decide(
            memory: memory,
            isActive: active,
            standing: admitted.Standing,
            turnedAway: admitted.TurnedAway,
            documentsAllowRelease: documentsAllowRelease,
            planHoldsTurnedAway: planHolds,
            markedRemovedFromErp: markedGone);
    }

    private static string Reason(ErpSupplier erp) => ErpImportAdmission.TurnedAwayReason(erp)!;

    [Fact]
    public void A_supplier_the_erp_lets_be_used_and_the_sync_never_acted_on_gets_no_change_and_no_note()
    {
        var decision = Decide(Erp());

        decision.Change.Should().Be(ErpDisabledChange.None);
        decision.Forecast.Should().Be(ErpDisabledChange.None);
        decision.HeldByLimit.Should().BeFalse();
        decision.Notes.Should().BeEmpty("a row that notes nothing happened is how an ordinary hourly run should read");
        decision.MarksSynced.Should().BeTrue();
        decision.AsksReinstatement.Should().BeFalse();
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, "Pending")]
    public void An_active_supplier_the_erp_turns_away_is_suspended_and_the_note_says_so(
        bool disabled, string? workflowState)
    {
        var erp = Erp(disabled, workflowState);

        var decision = Decide(erp);

        decision.Change.Should().Be(ErpDisabledChange.Suspended);
        decision.HeldByLimit.Should().BeFalse();
        decision.Notes.Should().ContainSingle()
            .Which.Should().Be(ErpImportAdmission.TurnedAwayNote(Reason(erp), ErpDisabledChange.Suspended));
    }

    [Fact]
    public void A_supplier_already_out_of_service_that_the_erp_turns_away_is_only_marked()
    {
        var erp = Erp(workflowState: "Pending");

        var decision = Decide(erp, active: false);

        decision.Change.Should().Be(ErpDisabledChange.Marked);
        decision.Notes.Should().ContainSingle()
            .Which.Should().Be(ErpImportAdmission.TurnedAwayNote(Reason(erp), ErpDisabledChange.Marked));
    }

    [Fact]
    public void A_supplier_the_sync_already_suspended_for_it_is_left_alone_and_the_note_does_not_claim_a_suspension()
    {
        var erp = Erp(disabled: true);

        var decision = Decide(erp, SupplierErpDisabledState.SuspendedAsDisabled, active: true);

        decision.Change.Should().Be(
            ErpDisabledChange.None, "a person reinstated it after the one suspension, and that decision stands");
        decision.Notes.Should().ContainSingle()
            .Which.Should().Be(ErpImportAdmission.TurnedAwayNote(Reason(erp), ErpDisabledChange.None));
    }

    [Fact]
    public void A_supplier_held_only_while_the_erp_approved_it_is_released_when_it_does()
    {
        var decision = Decide(Erp(), SupplierErpDisabledState.SuspendedAsPending, active: false);

        decision.Change.Should().Be(ErpDisabledChange.Released);
        decision.Notes.Should().Equal(ErpImportAdmission.ReleasedNote);
        decision.MarksSynced.Should().BeTrue();
    }

    [Fact]
    public void The_release_waits_while_an_award_critical_document_has_no_approved_renewal()
    {
        var decision = Decide(
            Erp(), SupplierErpDisabledState.SuspendedAsPending, active: false, documentsAllowRelease: false);

        decision.Change.Should().Be(ErpDisabledChange.ReleaseWaitsForDocuments);
        decision.Notes.Should().Equal(ErpImportAdmission.ReleaseWaitsNote);
    }

    [Fact]
    public void A_supplier_waiting_for_approval_that_the_erp_disables_instead_loses_its_release()
    {
        var erp = Erp(disabled: true);

        var decision = Decide(erp, SupplierErpDisabledState.SuspendedAsPending, active: false);

        decision.Change.Should().Be(ErpDisabledChange.ReleaseWithdrawn);
        decision.Notes.Should().ContainSingle()
            .Which.Should().Be(ErpImportAdmission.TurnedAwayNote(Reason(erp), ErpDisabledChange.ReleaseWithdrawn));
    }

    [Fact]
    public void A_supplier_marked_as_turned_away_that_the_erp_lets_be_used_again_is_cleared_and_reinstatement_is_asked()
    {
        var decision = Decide(Erp(), SupplierErpDisabledState.MarkedDisabled, active: false);

        decision.Change.Should().Be(ErpDisabledChange.Cleared);
        decision.Notes.Should().BeEmpty("the run says so only if the reinstatement it asks for happens");
        decision.AsksReinstatement.Should().BeTrue(
            "the mark may have held back a reinstatement after a renewal, and nothing else would look again");
    }

    [Fact]
    public void A_supplier_marked_as_gone_that_the_erp_returns_is_synced_and_reinstatement_is_asked()
    {
        var decision = Decide(Erp(), active: false, markedGone: true);

        decision.MarksSynced.Should().BeTrue("MarkSynced is what clears the mark");
        decision.AsksReinstatement.Should().BeTrue();
    }

    [Fact]
    public void A_change_the_plan_holds_back_is_not_made_and_the_note_says_it_was_held_back()
    {
        var erp = Erp(disabled: true);

        var decision = Decide(erp, planHolds: true);

        decision.HeldByLimit.Should().BeTrue();
        decision.Change.Should().Be(ErpDisabledChange.None, "the read that caused the hold is not believed");
        decision.Forecast.Should().Be(ErpDisabledChange.Suspended);
        decision.Notes.Should().ContainSingle().Which.Should().Be(ErpImportAdmission.HeldBackNote(Reason(erp)));
    }

    [Fact]
    public void A_hold_with_no_change_to_make_does_not_claim_one_was_held_back()
    {
        var erp = Erp(disabled: true);

        var decision = Decide(erp, SupplierErpDisabledState.SuspendedAsDisabled, active: false, planHolds: true);

        decision.HeldByLimit.Should().BeTrue();
        decision.Forecast.Should().Be(ErpDisabledChange.None);
        decision.Notes.Should().ContainSingle()
            .Which.Should().Be(ErpImportAdmission.TurnedAwayNote(Reason(erp), ErpDisabledChange.None));
    }

    [Fact]
    public void A_hold_keeps_a_mark_as_gone_so_nothing_reinstates_the_supplier_until_a_believed_run()
    {
        var decision = Decide(Erp(workflowState: "Pending"), active: false, planHolds: true, markedGone: true);

        decision.HeldByLimit.Should().BeTrue();
        decision.MarksSynced.Should().BeFalse("MarkSynced would clear the mark on the strength of a read nobody believes");
    }

    [Fact]
    public void A_hold_never_applies_to_a_supplier_the_erp_lets_be_used()
    {
        var decision = Decide(Erp(), SupplierErpDisabledState.SuspendedAsPending, active: false, planHolds: true);

        decision.HeldByLimit.Should().BeFalse("the plan holds back turned-away suppliers only");
        decision.Change.Should().Be(ErpDisabledChange.Released);
        decision.Notes.Should().Equal(ErpImportAdmission.ReleasedNote);
    }
}
