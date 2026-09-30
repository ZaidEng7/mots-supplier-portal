// The push to the ERP as the supplier record keeps it: what approval asks for, and the moves the push methods allow.
//
// APPROVAL ASKS ONLY WHILE THERE IS NO ExternalId. The suppliers imported from the ERP have one from the start, and a
// supplier approved again after a compliance edit has one once the push linked it. Asking for either would create a
// second supplier in the ERP, because the ERP makes one for every request. So the imported supplier and the linked one
// are tested as well as the plain case, which on its own would pass against an Approve that asked every time.
//
// EVERY PUSH METHOD IS TRIED FROM EVERY STATUS IT MUST REFUSE, not only from the one it accepts. A guard that let a
// retry through while an attempt was under way, or completed a push the ERP had no record for, would pass every test
// of the allowed move.
//
// THE PUSH NEVER WRITES WHAT THE SYNC REMEMBERS. The last tests put a pushed supplier into the two states the warning
// on SupplierSyncStatus is about - waiting for the ERP's approval, and suspended for leaving the ERP - and walk the
// whole push over it, as far as each lets it go: a supplier suspended for leaving the ERP is out of service, so its
// failed push is not retried. A push that called MarkSynced, or released the supplier, would change one of them.
//
// Each supplier is brought to its push status through the real methods, so every starting point is one the record
// actually produces.

namespace MotsSupplierPortal.Tests.Unit.Domain;

using FluentAssertions;
using MotsSupplierPortal.Domain.Suppliers;

public sealed class SupplierErpPushTests
{
    private const string ErpName = "SUP-2026-00092";

    private static readonly DateTimeOffset Now = new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);

    private static Supplier Imported() =>
        Supplier.ImportFromErp(
            "SUP-2026-000001", "SUP-2026-00001", "Homs Linen Mills", null, SupplierLegalType.Company, "SYP",
            "Homs Linen Mills", "sales@homslinen.example", null);

    private static Supplier InPushStatus(SupplierErpPushStatus status)
    {
        switch (status)
        {
            case SupplierErpPushStatus.NotRequested:
                return Supplier.Register(
                    "SUP-2026-000002", "شركة الاختبار", "Test Co", "CR-1", "Zaid", "zaid@example.com");

            case SupplierErpPushStatus.Requested:
                return SupplierTestFactory.Approved();

            case SupplierErpPushStatus.Linked:
            {
                var linked = SupplierTestFactory.Approved();
                linked.BeginErpPush(Now);
                linked.RecordErpSupplierCreated(ErpName, Now);
                return linked;
            }

            case SupplierErpPushStatus.Created:
            {
                var created = InPushStatus(SupplierErpPushStatus.Linked);
                created.CompleteErpPush();
                return created;
            }

            case SupplierErpPushStatus.Failed:
            {
                var failed = SupplierTestFactory.Approved();
                failed.BeginErpPush(Now);
                failed.FailErpPush("The ERP refused the supplier group.");
                return failed;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }
    }

    private static void SendBackToReviewAndApprove(Supplier supplier)
    {
        var reTriggered = supplier.UpdateLegalInfo(
            "شركة الاختبار", "Test Co", "CR-1", "TAX-2", SupplierLegalType.Company, null,
            isComplianceCritical: true);

        reTriggered.Should().BeTrue("a compliance edit on an approved supplier is what sends it back to review");
        supplier.OnboardingState.Should().Be(SupplierOnboardingState.UnderReview);

        supplier.Approve([]);
    }

    [Fact]
    public void Approving_a_supplier_that_registered_here_asks_for_it_to_be_created_in_the_erp()
    {
        var before = DateTimeOffset.UtcNow;

        var supplier = SupplierTestFactory.Approved();

        supplier.ErpPushStatus.Should().Be(SupplierErpPushStatus.Requested);
        supplier.ErpPushRequestedAt.Should().NotBeNull()
            .And.BeOnOrAfter(before)
            .And.BeOnOrBefore(DateTimeOffset.UtcNow);
        supplier.ErpPushNextAttemptAt.Should().Be(supplier.ErpPushRequestedAt, "the first attempt is due at once");
        supplier.ErpPushAttempts.Should().Be(0);
        supplier.ErpPushStartedAt.Should().BeNull();
        supplier.ExternalId.Should().BeNull("the ERP has no record of it until the push creates one");
    }

    [Fact]
    public void Approval_leaves_the_sync_state_and_the_rest_of_approval_as_they_were()
    {
        var supplier = SupplierTestFactory.Approved();

        supplier.OnboardingState.Should().Be(SupplierOnboardingState.Approved);
        supplier.LifecycleState.Should().Be(SupplierLifecycleState.Active);
        supplier.SyncStatus.Should().Be(SupplierSyncStatus.Pending);
        supplier.ErpDisabledState.Should().Be(SupplierErpDisabledState.NotDisabled);
        supplier.LastSyncedAt.Should().BeNull();
    }

    [Fact]
    public void A_supplier_imported_from_the_erp_is_never_asked_for_even_when_approved_again()
    {
        var supplier = Imported();
        supplier.ErpPushStatus.Should().Be(SupplierErpPushStatus.NotRequested);

        SendBackToReviewAndApprove(supplier);

        supplier.ErpPushStatus.Should().Be(
            SupplierErpPushStatus.NotRequested,
            "it is already in the ERP, and asking would create it there a second time");
        supplier.ErpPushRequestedAt.Should().BeNull();
        supplier.ErpPushNextAttemptAt.Should().BeNull();
        supplier.ExternalId.Should().Be("SUP-2026-00001");
        supplier.SyncStatus.Should().Be(SupplierSyncStatus.Synced);
    }

    [Fact]
    public void A_supplier_the_push_linked_is_not_asked_for_again_when_approved_again()
    {
        var supplier = InPushStatus(SupplierErpPushStatus.Created);
        var requestedAt = supplier.ErpPushRequestedAt;

        SendBackToReviewAndApprove(supplier);

        supplier.ErpPushStatus.Should().Be(SupplierErpPushStatus.Created);
        supplier.ErpPushRequestedAt.Should().Be(requestedAt);
        supplier.ErpPushNextAttemptAt.Should().BeNull();
        supplier.ExternalId.Should().Be(ErpName);
    }

    [Fact]
    public void A_supplier_approved_again_before_the_erp_has_it_is_asked_for_afresh_and_keeps_its_in_flight_marker()
    {
        var supplier = SupplierTestFactory.Approved();
        supplier.BeginErpPush(Now);
        supplier.RecordErpPushAttemptFailed("The ERP did not answer.", Now.AddHours(1));
        supplier.BeginErpPush(Now.AddHours(1));
        var approvedAgainFrom = DateTimeOffset.UtcNow;

        SendBackToReviewAndApprove(supplier);

        supplier.ErpPushStatus.Should().Be(SupplierErpPushStatus.Requested);
        supplier.ErpPushAttempts.Should().Be(0, "a person has just approved it, so the count starts again");
        supplier.ErpPushNextAttemptAt.Should().BeOnOrAfter(approvedAgainFrom, "the next attempt is due at once")
            .And.NotBe(Now.AddHours(1));
        supplier.ErpPushStartedAt.Should().Be(
            Now.AddHours(1),
            "that attempt may have reached the ERP, so the next one must still look before it creates");
    }

    // THE FIRST REQUEST'S TIME IS KEPT. The push looks for a create whose answer was lost among the ERP records made since
    // the request, and the first version moved the request to the new approval, so a create lost before it fell outside
    // the look and was posted a second time.
    [Fact]
    public void Approving_again_keeps_the_time_the_push_was_first_asked_for()
    {
        var supplier = SupplierTestFactory.Approved();
        var firstRequest = supplier.ErpPushRequestedAt;
        supplier.BeginErpPush(Now);
        supplier.FailErpPush("Stopped after 8 failed attempts. The last: the ERP did not answer.");

        SendBackToReviewAndApprove(supplier);

        supplier.ErpPushStatus.Should().Be(SupplierErpPushStatus.Requested);
        supplier.ErpPushRequestedAt.Should().Be(
            firstRequest, "the look for an earlier create reaches back from here, and must reach the first attempt");
        supplier.ErpPushNextAttemptAt.Should().BeOnOrAfter(firstRequest!.Value);
    }

    [Fact]
    public void A_push_that_failed_before_the_erp_had_it_is_asked_for_afresh_when_approved_again()
    {
        var supplier = InPushStatus(SupplierErpPushStatus.Failed);

        SendBackToReviewAndApprove(supplier);

        supplier.ErpPushStatus.Should().Be(SupplierErpPushStatus.Requested);
        supplier.ErpPushAttempts.Should().Be(0);
    }

    [Theory]
    [InlineData(SupplierErpPushStatus.Requested)]
    [InlineData(SupplierErpPushStatus.Linked)]
    public void Beginning_an_attempt_sets_the_in_flight_marker_and_keeps_the_status(SupplierErpPushStatus status)
    {
        var supplier = InPushStatus(status);

        supplier.BeginErpPush(Now.AddMinutes(5));

        supplier.ErpPushStartedAt.Should().Be(Now.AddMinutes(5));
        supplier.ErpPushStatus.Should().Be(status);
    }

    [Theory]
    [InlineData(SupplierErpPushStatus.NotRequested)]
    [InlineData(SupplierErpPushStatus.Created)]
    [InlineData(SupplierErpPushStatus.Failed)]
    public void Beginning_an_attempt_is_refused_when_no_push_is_under_way(SupplierErpPushStatus status)
    {
        var supplier = InPushStatus(status);

        var act = () => supplier.BeginErpPush(Now.AddMinutes(5));

        act.Should().Throw<DomainException>().WithMessage($"*'{status}'*only 'Requested' or 'Linked' is valid*");
        supplier.ErpPushStartedAt.Should().BeNull();
        supplier.ErpPushStatus.Should().Be(status);
    }

    [Fact]
    public void Recording_the_erp_supplier_saves_its_name_and_links_the_push_while_the_attempt_goes_on()
    {
        var supplier = SupplierTestFactory.Approved();
        supplier.BeginErpPush(Now);

        supplier.RecordErpSupplierCreated(ErpName, Now.AddMinutes(1));

        supplier.ExternalId.Should().Be(ErpName);
        supplier.ErpPushStatus.Should().Be(SupplierErpPushStatus.Linked);
        supplier.ErpPushNextAttemptAt.Should().Be(Now.AddMinutes(1), "the address, contact and user are due at once");
        supplier.ErpPushStartedAt.Should().Be(Now, "the attempt is still under way");
    }

    [Fact]
    public void Recording_the_same_erp_supplier_again_is_accepted()
    {
        var supplier = InPushStatus(SupplierErpPushStatus.Linked);

        supplier.RecordErpSupplierCreated(ErpName, Now.AddMinutes(1));

        supplier.ExternalId.Should().Be(ErpName);
        supplier.ErpPushStatus.Should().Be(SupplierErpPushStatus.Linked);
    }

    [Fact]
    public void Recording_a_different_erp_supplier_is_refused()
    {
        var supplier = InPushStatus(SupplierErpPushStatus.Linked);

        var act = () => supplier.RecordErpSupplierCreated("SUP-2026-00093", Now.AddMinutes(1));

        act.Should().Throw<DomainException>().WithMessage($"*already linked to '{ErpName}'*");
        supplier.ExternalId.Should().Be(ErpName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Recording_an_erp_supplier_with_no_name_is_refused(string erpName)
    {
        var supplier = SupplierTestFactory.Approved();

        var act = () => supplier.RecordErpSupplierCreated(erpName, Now);

        act.Should().Throw<DomainException>();
        supplier.ExternalId.Should().BeNull();
        supplier.ErpPushStatus.Should().Be(SupplierErpPushStatus.Requested);
    }

    [Theory]
    [InlineData(SupplierErpPushStatus.NotRequested)]
    [InlineData(SupplierErpPushStatus.Created)]
    [InlineData(SupplierErpPushStatus.Failed)]
    public void Recording_the_erp_supplier_is_refused_when_no_push_is_under_way(SupplierErpPushStatus status)
    {
        var supplier = InPushStatus(status);
        var externalId = supplier.ExternalId;

        var act = () => supplier.RecordErpSupplierCreated(ErpName, Now);

        act.Should().Throw<DomainException>().WithMessage($"*'{status}'*only 'Requested' or 'Linked' is valid*");
        supplier.ExternalId.Should().Be(externalId);
        supplier.ErpPushStatus.Should().Be(status);
    }

    [Fact]
    public void Completing_a_linked_push_clears_the_marker_the_error_and_the_due_time()
    {
        var supplier = InPushStatus(SupplierErpPushStatus.Linked);
        supplier.RecordErpPushAttemptFailed("The ERP refused the address.", Now.AddHours(1));
        supplier.BeginErpPush(Now.AddHours(1));

        supplier.CompleteErpPush();

        supplier.ErpPushStatus.Should().Be(SupplierErpPushStatus.Created);
        supplier.ErpPushStartedAt.Should().BeNull();
        supplier.ErpPushNextAttemptAt.Should().BeNull();
        supplier.ErpPushLastError.Should().BeNull();
        supplier.ExternalId.Should().Be(ErpName);
    }

    [Theory]
    [InlineData(SupplierErpPushStatus.NotRequested)]
    [InlineData(SupplierErpPushStatus.Requested)]
    [InlineData(SupplierErpPushStatus.Created)]
    [InlineData(SupplierErpPushStatus.Failed)]
    public void Completing_is_refused_unless_the_erp_has_the_supplier_record(SupplierErpPushStatus status)
    {
        var supplier = InPushStatus(status);

        var act = supplier.CompleteErpPush;

        act.Should().Throw<DomainException>().WithMessage($"*'{status}'*only 'Linked' is valid*");
        supplier.ErpPushStatus.Should().Be(status);
    }

    [Theory]
    [InlineData(SupplierErpPushStatus.Requested)]
    [InlineData(SupplierErpPushStatus.Linked)]
    public void A_failed_attempt_is_counted_and_the_next_one_is_due_later(SupplierErpPushStatus status)
    {
        var supplier = InPushStatus(status);
        supplier.BeginErpPush(Now);

        supplier.RecordErpPushAttemptFailed("The ERP did not answer.", Now.AddMinutes(10));

        supplier.ErpPushAttempts.Should().Be(1);
        supplier.ErpPushStatus.Should().Be(status);
        supplier.ErpPushStartedAt.Should().BeNull("the attempt is over");
        supplier.ErpPushNextAttemptAt.Should().Be(Now.AddMinutes(10));
        supplier.ErpPushLastError.Should().Be("The ERP did not answer.");

        supplier.BeginErpPush(Now.AddMinutes(10));
        supplier.RecordErpPushAttemptFailed("The ERP answered 503.", Now.AddMinutes(30));

        supplier.ErpPushAttempts.Should().Be(2);
        supplier.ErpPushLastError.Should().Be("The ERP answered 503.");
    }

    [Fact]
    public void A_long_error_is_cut_to_the_column_rather_than_refused()
    {
        var supplier = SupplierTestFactory.Approved();

        supplier.RecordErpPushAttemptFailed(new string('x', 600), Now);

        supplier.ErpPushLastError.Should().HaveLength(Supplier.ErpPushLastErrorMaxLength).And.EndWith("…");
    }

    [Theory]
    [InlineData(SupplierErpPushStatus.NotRequested)]
    [InlineData(SupplierErpPushStatus.Created)]
    [InlineData(SupplierErpPushStatus.Failed)]
    public void Recording_a_failed_attempt_is_refused_when_no_push_is_under_way(SupplierErpPushStatus status)
    {
        var supplier = InPushStatus(status);
        var attempts = supplier.ErpPushAttempts;

        var act = () => supplier.RecordErpPushAttemptFailed("The ERP did not answer.", Now);

        act.Should().Throw<DomainException>().WithMessage($"*'{status}'*only 'Requested' or 'Linked' is valid*");
        supplier.ErpPushAttempts.Should().Be(attempts);
        supplier.ErpPushStatus.Should().Be(status);
    }

    [Theory]
    [InlineData(SupplierErpPushStatus.Requested)]
    [InlineData(SupplierErpPushStatus.Linked)]
    public void Failing_the_push_stops_it_until_a_person_retries(SupplierErpPushStatus status)
    {
        var supplier = InPushStatus(status);
        supplier.BeginErpPush(Now);

        supplier.FailErpPush("The ERP refused the supplier group.");

        supplier.ErpPushStatus.Should().Be(SupplierErpPushStatus.Failed);
        supplier.ErpPushLastError.Should().Be("The ERP refused the supplier group.");
        supplier.ErpPushAttempts.Should().Be(1, "the attempt that failed counts, as any other failed attempt does");
        supplier.ErpPushStartedAt.Should().BeNull();
        supplier.ErpPushNextAttemptAt.Should().BeNull("nothing is due until a person retries");
    }

    [Theory]
    [InlineData(SupplierErpPushStatus.NotRequested)]
    [InlineData(SupplierErpPushStatus.Created)]
    [InlineData(SupplierErpPushStatus.Failed)]
    public void Failing_is_refused_when_no_push_is_under_way(SupplierErpPushStatus status)
    {
        var supplier = InPushStatus(status);

        var act = () => supplier.FailErpPush("The ERP refused the supplier group.");

        act.Should().Throw<DomainException>().WithMessage($"*'{status}'*only 'Requested' or 'Linked' is valid*");
        supplier.ErpPushStatus.Should().Be(status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_failure_with_no_message_is_refused(string message)
    {
        var supplier = SupplierTestFactory.Approved();

        ((Action)(() => supplier.FailErpPush(message))).Should().Throw<DomainException>();
        ((Action)(() => supplier.RecordErpPushAttemptFailed(message, Now))).Should().Throw<DomainException>();

        supplier.ErpPushStatus.Should().Be(SupplierErpPushStatus.Requested);
        supplier.ErpPushAttempts.Should().Be(0);
    }

    [Fact]
    public void Retrying_a_push_that_failed_before_the_erp_had_it_asks_for_the_create_again()
    {
        var supplier = InPushStatus(SupplierErpPushStatus.Failed);

        supplier.RetryErpPush(Now.AddHours(2));

        supplier.ErpPushStatus.Should().Be(SupplierErpPushStatus.Requested);
        supplier.ErpPushAttempts.Should().Be(0);
        supplier.ErpPushNextAttemptAt.Should().Be(Now.AddHours(2));
        supplier.ErpPushLastError.Should().Be(
            "The ERP refused the supplier group.", "the screen can still say why it failed until an attempt says more");
    }

    [Fact]
    public void Retrying_a_push_that_failed_after_the_erp_had_it_carries_on_from_the_link()
    {
        var supplier = InPushStatus(SupplierErpPushStatus.Linked);
        supplier.FailErpPush("The ERP refused the contact.");

        supplier.RetryErpPush(Now.AddHours(2));

        supplier.ErpPushStatus.Should().Be(
            SupplierErpPushStatus.Linked, "going back to Requested would create the ERP supplier a second time");
        supplier.ExternalId.Should().Be(ErpName);
        supplier.ErpPushAttempts.Should().Be(0);
    }

    // A SUPPLIER A PERSON TOOK OUT OF SERVICE IS NOT RETRIED. The push would create it in the ERP with a website user
    // holding the Supplier role; the first version let a retry through for a supplier deactivated for good.
    [Theory]
    [InlineData(SupplierLifecycleState.Suspended)]
    [InlineData(SupplierLifecycleState.Deactivated)]
    public void Retrying_is_refused_for_a_supplier_a_person_took_out_of_service(SupplierLifecycleState lifecycle)
    {
        var supplier = InPushStatus(SupplierErpPushStatus.Failed);
        supplier.Suspend("Under investigation.");
        if (lifecycle == SupplierLifecycleState.Deactivated) supplier.Deactivate("Fraud confirmed.");

        supplier.IsInServiceForErpPush.Should().BeFalse();

        var act = () => supplier.RetryErpPush(Now.AddHours(2));

        act.Should().Throw<DomainException>().WithMessage($"*out of service*'{lifecycle}'*");
        supplier.ErpPushStatus.Should().Be(SupplierErpPushStatus.Failed);
    }

    [Fact]
    public void Retrying_is_allowed_while_only_the_sync_holds_the_supplier_for_the_erps_approval()
    {
        var supplier = InPushStatus(SupplierErpPushStatus.Linked);
        supplier.FailErpPush("The ERP refused the contact's phone.");
        supplier.MarkSynced(ErpName);
        supplier.RecordErpStanding(ErpStanding.AwaitingApproval).Should().Be(ErpDisabledChange.Suspended);

        supplier.IsInServiceForErpPush.Should().BeTrue(
            "the Draft record the push made is what put the supplier there, and its contact is part of what the ERP "
            + "team approves");

        supplier.RetryErpPush(Now.AddHours(2));

        supplier.ErpPushStatus.Should().Be(SupplierErpPushStatus.Linked);
    }

    [Fact]
    public void A_person_suspending_a_supplier_the_sync_held_takes_it_out_of_service_for_the_push()
    {
        var supplier = InPushStatus(SupplierErpPushStatus.Linked);
        supplier.MarkSynced(ErpName);
        supplier.RecordErpStanding(ErpStanding.AwaitingApproval);

        supplier.Suspend("Held by the ministry.");

        supplier.IsInServiceForErpPush.Should().BeFalse("the suspension is now a person's, not the sync's hold");
    }

    [Theory]
    [InlineData(SupplierErpPushStatus.NotRequested)]
    [InlineData(SupplierErpPushStatus.Requested)]
    [InlineData(SupplierErpPushStatus.Linked)]
    [InlineData(SupplierErpPushStatus.Created)]
    public void Retrying_is_refused_unless_the_push_failed(SupplierErpPushStatus status)
    {
        var supplier = InPushStatus(status);

        var act = () => supplier.RetryErpPush(Now.AddHours(2));

        act.Should().Throw<DomainException>().WithMessage($"*'{status}'*only 'Failed' is valid*");
        supplier.ErpPushStatus.Should().Be(status);
    }

    [Theory]
    [InlineData("waiting for the ERP's approval")]
    [InlineData("suspended for leaving the ERP")]
    public void The_push_never_writes_what_the_sync_remembers_or_the_lifecycle(string syncMemory)
    {
        var supplier = SupplierTestFactory.Approved();
        supplier.BeginErpPush(Now);
        supplier.RecordErpSupplierCreated(ErpName, Now);
        supplier.MarkSynced(ErpName);

        if (syncMemory == "waiting for the ERP's approval")
        {
            supplier.RecordErpStanding(ErpStanding.AwaitingApproval).Should().Be(ErpDisabledChange.Suspended);
            supplier.ErpDisabledState.Should().Be(SupplierErpDisabledState.SuspendedAsPending);
        }
        else
        {
            supplier.SuspendAsRemovedFromErp();
            supplier.SyncStatus.Should().Be(SupplierSyncStatus.RemovedFromErp);
        }

        var remembered = (supplier.SyncStatus, supplier.LastSyncedAt, supplier.ErpDisabledState,
            supplier.LifecycleState, supplier.OnboardingState);

        void AfterEachStep(Action step)
        {
            step();
            (supplier.SyncStatus, supplier.LastSyncedAt, supplier.ErpDisabledState,
                supplier.LifecycleState, supplier.OnboardingState).Should().Be(remembered);
        }

        AfterEachStep(() => supplier.RecordErpPushAttemptFailed("The ERP refused the address.", Now.AddHours(1)));
        AfterEachStep(() => supplier.BeginErpPush(Now.AddHours(1)));
        AfterEachStep(() => supplier.RecordErpSupplierCreated(ErpName, Now.AddHours(1)));
        AfterEachStep(() => supplier.FailErpPush("The ERP refused the address."));

        if (syncMemory == "suspended for leaving the ERP")
        {
            AfterEachStep(() => supplier.Invoking(s => s.RetryErpPush(Now.AddHours(2))).Should().Throw<DomainException>(
                "the sync's suspension for leaving the ERP takes the supplier out of service, and the push would only "
                + "link records to a supplier the ERP no longer returns"));

            supplier.ErpPushStatus.Should().Be(SupplierErpPushStatus.Failed);
            return;
        }

        AfterEachStep(() => supplier.RetryErpPush(Now.AddHours(2)));
        AfterEachStep(() => supplier.BeginErpPush(Now.AddHours(2)));
        AfterEachStep(supplier.CompleteErpPush);

        supplier.ErpPushStatus.Should().Be(SupplierErpPushStatus.Created);
    }
}
