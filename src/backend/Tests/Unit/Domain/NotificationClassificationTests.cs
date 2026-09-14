// A user may opt out of informational notifications and not of actionable ones.
//
//
// THE EXHAUSTIVENESS ASSERTION IS THE LOAD-BEARING ONE
//
// The classification is only worth anything if it covers every type, and the way it stops covering every type is
// that somebody adds a thirty-third notification and nobody classifies it.
//
// That is what the first assertion refuses, and it is why the two sets are declared explicitly rather than one
// being derived as everything else.
//
//
// THE FOUR FAMILIES THE DECISION NAMED
//
// Invitations, clarification requests, award outcomes and document expiry must never be muteable. The decision
// named them as the constraint the classification has to satisfy rather than as the whole answer.
//
// Three are pinned here. The invitation is pinned as a bidder experiences it, which is the tender opening,
// because the invitation itself is recorded while the tender is still a draft. Clarifications are pinned in both
// directions, because the question asked of a supplier and the question asked of a buyer are the same
// unanswerable message if it does not arrive. Award outcomes include the offer itself, which expires if the
// supplier does not answer it.
//
// The fourth, document expiry, has no notification type at all: it is an email reminder path, recorded in the
// classification's own header so that a type added there later inherits the ruling rather than a default.
//
// The classification is fail-closed, and the asymmetry is the point: over-delivery irritates somebody, and
// under-delivery is a contract award nobody was told about.
//
//
// THE CONTROL
//
// Every assertion above passes just as well against a classification that mutes nothing at all, which would
// satisfy the decision's constraint and defeat its purpose, because the preferences screen it exists for would
// then have nothing to offer.

namespace MotsSupplierPortal.Tests.Unit.Domain;

using FluentAssertions;
using MotsSupplierPortal.Domain.Notifications;

public class NotificationClassificationTests
{
    [Fact]
    public void Every_notification_type_is_classified_exactly_once()
    {
        var classified = NotificationClassification.Actionable
            .Concat(NotificationClassification.Informational).ToList();

        classified.Should().OnlyHaveUniqueItems("a type in both sets has no answer, only two");
        classified.Should().BeEquivalentTo(NotificationTypes.All,
            "a notification nobody classified is a preferences screen deciding by accident what D-60 " +
            "decided on purpose");
    }

    [Fact]
    public void The_families_D60_names_are_never_muteable()
    {
        NotificationClassification.IsMuteable(NotificationTypes.RfqSubmissionOpened).Should().BeFalse();

        NotificationClassification.IsMuteable(NotificationTypes.RfqClarificationRequested).Should().BeFalse();
        NotificationClassification.IsMuteable(NotificationTypes.ProposalClarificationRequested).Should().BeFalse();

        NotificationClassification.IsMuteable(NotificationTypes.AwardApproved).Should().BeFalse();
        NotificationClassification.IsMuteable(NotificationTypes.AwardRejected).Should().BeFalse();
        NotificationClassification.IsMuteable(NotificationTypes.ProposalAwardOffered).Should().BeFalse();
    }

    [Fact]
    public void An_unrecognised_type_is_delivered_rather_than_suppressed()
    {
        NotificationClassification.IsMuteable("not.a.real.type").Should().BeFalse();
        NotificationClassification.IsActionable("not.a.real.type").Should().BeTrue();
    }

    [Fact]
    public void Something_is_actually_muteable()
    {
        NotificationClassification.Informational.Should().NotBeEmpty();
        NotificationClassification.IsMuteable(NotificationTypes.EvaluatorSubmitted).Should().BeTrue();
    }
}
