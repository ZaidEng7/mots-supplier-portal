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
        "erp-supplier-sync",
        "supplier-erp-push",
    ];

    // Jobs the generic "Run now" button must not start, each with the screen that starts it properly instead.
    //
    // The supplier import changes the registry the ministry reads - it creates suppliers and suspends them - so it is
    // started only where its own permission is checked and the person who pressed the button is named on every row it
    // writes. Run from the operations screen it ran as "system" under a different permission, and nothing recorded
    // who had clicked.
    public static readonly IReadOnlyDictionary<string, string> StartedFromTheirOwnScreen =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["erp-supplier-sync"] = "/back-office/erp-import",
        };
}
