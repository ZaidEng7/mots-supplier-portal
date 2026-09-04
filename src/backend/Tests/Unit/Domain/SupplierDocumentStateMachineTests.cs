using System.Reflection;
using FluentAssertions;
using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Tests.Unit.Domain;

/// <summary>
/// MSP-68: the document state machine had ZERO domain unit tests, while NFR-CMP-003 requires them
/// per state machine and the onboarding machine has good ones.
///
/// This is written first because everything else in MSP-68 touches these transitions. The tests are
/// the safety net the rest of the ticket leans on, not a formality after it.
///
/// The exhaustive theories use the technique that already earned itself in the eligibility work: a
/// closed set of states is asserted to BE covered, rather than trusting whoever wrote the list to
/// have enumerated it. That assertion caught a missing onboarding state on its first run.
/// </summary>
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
            // Forced, for the same reason SupplierTestFactory forces onboarding states: the point of
            // an exhaustive theory is to cover combinations the aggregate cannot currently reach,
            // because those are exactly where a future transition could quietly permit something.
            typeof(SupplierDocument)
                .GetProperty(nameof(SupplierDocument.State), BindingFlags.Public | BindingFlags.Instance)!
                .SetMethod!.Invoke(document, [state]);
        }

        return document;
    }

    /// <summary>Every state the machine defines. The guard test below fails if the enum grows and
    /// this list is not updated, so a new state cannot inherit its transition rules by omission.</summary>
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

    // ---- the happy path, end to end -----------------------------------------------------

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

    // ---- MarkScanClean / MarkScanRejected ------------------------------------------------

    [Theory]
    [MemberData(nameof(StatesOtherThan), DocumentState.PendingScan)]
    public void Scan_results_are_rejected_from_any_state_but_PendingScan(DocumentState state)
    {
        // Both directions of the scan result, because a document that has already been scanned
        // must not be re-decided by a late or duplicate AV callback.
        InState(state).Invoking(d => d.MarkScanClean("clean/key")).Should().Throw<DomainException>();
        InState(state).Invoking(d => d.MarkScanRejected()).Should().Throw<DomainException>();
    }

    // ---- Approve / Reject ----------------------------------------------------------------

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
        // Covers PendingScan and ScanRejected (not yet scanned clean) and Approved, Rejected,
        // ExpiringSoon, Expired (already settled). A second approval on an approved document would
        // silently overwrite who reviewed it and when.
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

    // ---- expiry ---------------------------------------------------------------------------

    [Fact]
    public void Only_an_approved_document_can_become_expiring_soon()
    {
        InState(DocumentState.Approved).Invoking(d => d.MarkExpiringSoon()).Should().NotThrow();
    }

    [Theory]
    [MemberData(nameof(StatesOtherThan), DocumentState.Approved)]
    public void Expiring_soon_is_rejected_from_every_other_state(DocumentState state)
    {
        // Notably including ExpiringSoon itself: re-marking would let the expiry job re-notify on
        // every run, which is the de-duplication concern FR-NOT-006 raises.
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

        // Renewal is a NEW version, not a resurrection of the expired row - which is what
        // SupersedeWithNewVersion exists for and why history survives.
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
