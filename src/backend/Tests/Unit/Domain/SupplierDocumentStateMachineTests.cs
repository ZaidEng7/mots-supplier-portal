// The document state machine, which had no domain tests at all.
//
// The written requirements ask for tests per state machine, and the onboarding machine has good ones.
//
// This was written first because everything else in that piece of work touches these transitions. The tests are
// the safety net the rest of it leans on rather than a formality after it.
//
//
// THE EXHAUSTIVE THEORIES ASSERT THEIR OWN COVERAGE
//
// A closed set of states is asserted to BE covered rather than trusting whoever wrote the list to have
// enumerated it. That assertion caught a missing onboarding state on its first run.
//
// The guard test fails if the set of states grows and the list is not updated, so a new state cannot inherit its
// transition rules by omission.
//
// States are forced where the aggregate cannot currently reach them, for the same reason the shared factory
// forces onboarding states: the point of an exhaustive theory is to cover combinations that are unreachable
// today, because those are exactly where a future transition could quietly permit something.
//
//
// WHAT EACH GROUP COVERS
//
// The scan result is asserted in both directions, because a document that has already been scanned must not be
// re-decided by a late or duplicate callback from the scanner.
//
// A second decision is refused from the not-yet-scanned states and from the already-settled ones. A second
// approval on an approved document would silently overwrite who reviewed it and when.
//
// Re-marking a document as expiring is refused, notably including from the expiring state itself, because
// otherwise the expiry job would re-notify on every run, which is the de-duplication concern the written
// requirement raises.
//
// And renewal is a NEW version rather than a resurrection of the expired row, which is what superseding exists
// for and why the history survives.

namespace MotsSupplierPortal.Tests.Unit.Domain;

using System.Reflection;
using FluentAssertions;
using MotsSupplierPortal.Domain.Suppliers;

public sealed class SupplierDocumentStateMachineTests
{
    private static SupplierDocument InState(DocumentState state)
    {
        var document = SupplierDocument.CreatePendingScan(
            $"DOC-2026-{Guid.NewGuid().ToString("N")[..6]}",
            Guid.CreateVersion7(), Guid.CreateVersion7(), 1, "quarantine/key",
            "cert.pdf", "application/pdf", 1024, Guid.CreateVersion7(),
            issueDate: null, expiryDate: null,
            expiryTracked: false, today: DateOnly.FromDateTime(DateTime.UtcNow));

        if (state != DocumentState.PendingScan)
        {
            typeof(SupplierDocument)
                .GetProperty(nameof(SupplierDocument.State), BindingFlags.Public | BindingFlags.Instance)!
                .SetMethod!.Invoke(document, [state]);
        }

        return document;
    }

    private static readonly DocumentState[] AllStates =
    [
        DocumentState.PendingScan, DocumentState.ScanRejected, DocumentState.Uploaded,
        DocumentState.UnderReview, DocumentState.Approved, DocumentState.Rejected,
        DocumentState.ExpiringSoon, DocumentState.Expired,
    ];

    [Fact]
    public void Every_document_state_is_covered_by_these_tests()
    {
        Enum.GetValues<DocumentState>().Should().BeSubsetOf(AllStates,
            "a state added later must be given transition rules deliberately; inheriting them by " +
            "omission is how a machine silently permits something nobody decided");
    }

    [Fact]
    public void Clean_scan_carries_a_document_from_upload_to_approved()
    {
        var document = InState(DocumentState.PendingScan);

        document.MarkScanClean("clean/key");
        document.State.Should().Be(DocumentState.Uploaded);
        document.StorageKey.Should().Be("clean/key",
            "the object moves out of quarantine as part of the transition, not separately");

        document.Approve(Guid.CreateVersion7());
        document.State.Should().Be(DocumentState.Approved);
    }

    [Theory]
    [MemberData(nameof(StatesOtherThan), DocumentState.PendingScan)]
    public void Scan_results_are_rejected_from_any_state_but_PendingScan(DocumentState state)
    {
        InState(state).Invoking(d => d.MarkScanClean("clean/key")).Should().Throw<DomainException>();
        InState(state).Invoking(d => d.MarkScanRejected()).Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(DocumentState.Uploaded)]
    [InlineData(DocumentState.UnderReview)]
    public void A_reviewer_can_decide_a_scanned_document(DocumentState state)
    {
        var reviewer = Guid.CreateVersion7();

        var approved = InState(state);
        approved.Approve(reviewer);
        approved.State.Should().Be(DocumentState.Approved);
        approved.ReviewedByUserId.Should().Be(reviewer, "the decision records who made it");

        var rejected = InState(state);
        rejected.Reject(reviewer, "Illegible scan");
        rejected.State.Should().Be(DocumentState.Rejected);
        rejected.RejectReason.Should().Be("Illegible scan");
    }

    [Theory]
    [MemberData(nameof(StatesOtherThan2), DocumentState.Uploaded, DocumentState.UnderReview)]
    public void A_document_cannot_be_decided_before_it_has_been_scanned_or_after_it_is_settled(DocumentState state)
    {
        InState(state).Invoking(d => d.Approve(Guid.CreateVersion7())).Should().Throw<DomainException>();
        InState(state).Invoking(d => d.Reject(Guid.CreateVersion7(), "Reason")).Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Rejection_requires_a_reason(string? reason)
    {
        var document = InState(DocumentState.Uploaded);

        document.Invoking(d => d.Reject(Guid.CreateVersion7(), reason!))
            .Should().Throw<DomainException>().WithMessage("*reason is required*");
    }

    [Fact]
    public void Only_an_approved_document_can_become_expiring_soon()
    {
        InState(DocumentState.Approved).Invoking(d => d.MarkExpiringSoon()).Should().NotThrow();
    }

    [Theory]
    [MemberData(nameof(StatesOtherThan), DocumentState.Approved)]
    public void Expiring_soon_is_rejected_from_every_other_state(DocumentState state)
    {
        InState(state).Invoking(d => d.MarkExpiringSoon()).Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(DocumentState.Approved)]
    [InlineData(DocumentState.ExpiringSoon)]
    public void A_live_document_can_expire(DocumentState state)
    {
        var document = InState(state);

        document.MarkExpired();

        document.State.Should().Be(DocumentState.Expired);
    }

    [Theory]
    [MemberData(nameof(StatesOtherThan2), DocumentState.Approved, DocumentState.ExpiringSoon)]
    public void A_document_that_was_never_live_cannot_expire(DocumentState state)
    {
        InState(state).Invoking(d => d.MarkExpired()).Should().Throw<DomainException>();
    }

    [Fact]
    public void Expired_is_terminal()
    {
        var document = InState(DocumentState.Expired);

        document.Invoking(d => d.MarkExpiringSoon()).Should().Throw<DomainException>();
        document.Invoking(d => d.MarkExpired()).Should().Throw<DomainException>();
        document.Invoking(d => d.Approve(Guid.CreateVersion7())).Should().Throw<DomainException>();

        document.State.Should().Be(DocumentState.Expired);
    }

    [Fact]
    public void Superseding_marks_the_old_version_as_no_longer_latest()
    {
        var document = InState(DocumentState.Approved);

        document.SupersedeWithNewVersion();

        document.IsLatestVersion.Should().BeFalse(
            "expiry and completeness queries filter on IsLatestVersion, so an old version that " +
            "stays 'latest' would keep a renewed document looking expired");
    }

    public static TheoryData<DocumentState> StatesOtherThan(DocumentState excluded)
    {
        var data = new TheoryData<DocumentState>();
        foreach (var state in AllStates.Where(s => s != excluded))
        {
            data.Add(state);
        }
        return data;
    }

    public static TheoryData<DocumentState> StatesOtherThan2(DocumentState first, DocumentState second)
    {
        var data = new TheoryData<DocumentState>();
        foreach (var state in AllStates.Where(s => s != first && s != second))
        {
            data.Add(state);
        }
        return data;
    }
}
