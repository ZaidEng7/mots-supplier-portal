using FluentAssertions;
using MotsSupplierPortal.Domain.Notifications;

namespace MotsSupplierPortal.Tests.Unit.Domain;

/// <summary>
/// D-60: a user may opt out of informational notifications and not of actionable ones.
///
/// <para><b>The exhaustiveness assertion is the load-bearing one.</b> The classification is only worth
/// anything if it covers every type, and the way it stops covering every type is that somebody adds a
/// thirty-third notification and nobody classifies it. That is what the first test refuses, and it is why
/// the two sets are declared explicitly rather than one being derived as "everything else".</para>
///
/// <para>D-60 named four families that must never be muteable - invitations, clarification requests, award
/// outcomes and document expiry - as the constraint the classification has to satisfy rather than as the
/// whole answer. Three are pinned below. The fourth, document expiry, has no notification type at all: it
/// is an email reminder path, recorded in <see cref="NotificationClassification"/>'s own comment so that a
/// type added there later inherits the ruling rather than a default.</para>
/// </summary>
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
        // The invitation, as a bidder experiences it: the tender opening is what reaches an invited
        // supplier, because the invitation itself is recorded while the tender is still Draft.
        NotificationClassification.IsMuteable(NotificationTypes.RfqSubmissionOpened).Should().BeFalse();

        // Clarifications, both directions - the question asked of a supplier and the question asked of a
        // buyer are the same unanswerable message if it does not arrive.
        NotificationClassification.IsMuteable(NotificationTypes.RfqClarificationRequested).Should().BeFalse();
        NotificationClassification.IsMuteable(NotificationTypes.ProposalClarificationRequested).Should().BeFalse();

        // Award outcomes, including the offer itself, which expires if the supplier does not answer it.
        NotificationClassification.IsMuteable(NotificationTypes.AwardApproved).Should().BeFalse();
        NotificationClassification.IsMuteable(NotificationTypes.AwardRejected).Should().BeFalse();
        NotificationClassification.IsMuteable(NotificationTypes.ProposalAwardOffered).Should().BeFalse();
    }

    [Fact]
    public void An_unrecognised_type_is_delivered_rather_than_suppressed()
    {
        // Fail-closed, and the asymmetry is the point: over-delivery irritates somebody, under-delivery is
        // a contract award nobody was told about.
        NotificationClassification.IsMuteable("not.a.real.type").Should().BeFalse();
        NotificationClassification.IsActionable("not.a.real.type").Should().BeTrue();
    }

    [Fact]
    public void Something_is_actually_muteable()
    {
        // The control. Every assertion above passes just as well against a classification that mutes
        // nothing at all - which would satisfy D-60's constraint and defeat its purpose, since the
        // preferences screen it exists for would then have nothing to offer.
        NotificationClassification.Informational.Should().NotBeEmpty();
        NotificationClassification.IsMuteable(NotificationTypes.EvaluatorSubmitted).Should().BeTrue();
    }
}
