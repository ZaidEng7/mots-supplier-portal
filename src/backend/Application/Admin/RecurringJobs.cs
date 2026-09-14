// The recurring jobs this application registers, named once.
//
// It is shared between the registration at start-up and the administrator dashboard's health figure, so the
// two cannot disagree about what this application schedules.
//
// It is deliberately not shared with the test that asserts them. That test keeps its own list on purpose,
// because it exists to catch a job added or removed without anybody noticing, and a test reading the same
// constant the application reads cannot catch that. Two independent statements of the same fact is the point
// there. Two copies inside the application is not, which is what this removes.

namespace MotsSupplierPortal.Application.Admin;

public static class RecurringJobs
{
    public static readonly string[] All =
    [
        "document-expiry-lifecycle",
        "draft-registration-cleanup",
        "outbox-dispatch",
        "rfq-timeline",
        "award-erp-sync",
        "idempotency-cleanup",
    ];
}
