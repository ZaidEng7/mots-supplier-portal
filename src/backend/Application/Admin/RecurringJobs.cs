namespace MotsSupplierPortal.Application.Admin;

/// <summary>
/// The recurring jobs this application registers, named once.
///
/// <para><b>Shared between the registration in Program.cs and the admin dashboard's health check -
/// and deliberately NOT with the test that asserts them.</b> RecurringJobSuppressionTests keeps its
/// own list on purpose: it exists to catch a job that was added or removed without anyone noticing,
/// and a test reading the same constant the application reads cannot catch that. Two independent
/// statements of the same fact is the point there. Two copies inside the application is not, which
/// is what this removes.</para>
/// </summary>
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
