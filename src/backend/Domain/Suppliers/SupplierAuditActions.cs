// The audit actions the automatic paths write on a supplier's trail: the document expiry rule, the automatic
// reinstatement, the ERP import, whether a person starts it or the hourly sync does, and the push that creates a
// supplier approved here in the ERP.
//
// THESE VALUES ARE STORED IN THE AUDIT LOG AND READ BACK BY CODE, SO A VALUE IS NEVER RENAMED. AutomaticReinstatement
// lifts a suspension only when the latest row that suspended the supplier carries AutoSuspended; renamed, every
// supplier the expiry rule suspended would stay suspended after its renewal was approved, and nothing would report
// it. The ErpImportAuditTrail migration reads ErpImportRun, ImportedFromErp and ErpReleaseWithdrawn and writes three of
// the ERP actions in its own SQL, and the audit log's action filter matches the exact text, so a renamed value would
// also split one kind of event into two names across the rows already written. A constant's name may change; its
// value may not.
//
// THE SPELLINGS DIFFER - supplier_auto_suspended, supplier.imported_from_erp, ErpImportRun - because each was chosen
// when its writer was written, and the rows already hold them.
//
// THE TESTS KEEP THE LITERAL VALUES on purpose. A test that used these constants would follow a rename and pass; a
// test holding the text is what catches one.
//
// The actions a person's decision writes - suspended, reactivated, deactivated, kept suspended - are written by
// SupplierLifecycleHandler, and the preview's ErpImportPreviewed by PreviewErpImportHandler; they are not listed here.

namespace MotsSupplierPortal.Domain.Suppliers;

public static class SupplierAuditActions
{
    // The document expiry rule suspends a supplier whose award-critical document expired, and the automatic
    // reinstatement lifts that suspension, and only that one, once the renewal is approved.
    public const string AutoSuspended = "supplier_auto_suspended";
    public const string AutoReinstated = "supplier_auto_reinstated";

    // ErpImportRun is the run's own row, not a supplier's: written once per run, under the Supplier aggregate with an
    // empty identifier, before the first supplier is touched. ImportedFromErp is a supplier the run created.
    public const string ErpImportRun = "ErpImportRun";
    public const string ImportedFromErp = "supplier.imported_from_erp";

    // A supplier the ERP no longer returns, disables or has not approved is suspended once if it is active, and only
    // marked if it is out of service already.
    public const string SuspendedMissingFromErp = "supplier.suspended_missing_from_erp";
    public const string SuspendedDisabledInErp = "supplier.suspended_disabled_in_erp";
    public const string SuspendedNotApprovedInErp = "supplier.suspended_not_approved_in_erp";
    public const string MarkedRemovedFromErp = "supplier.marked_removed_from_erp";
    public const string MarkedDisabledInErp = "supplier.marked_disabled_in_erp";
    public const string MarkedNotApprovedInErp = "supplier.marked_not_approved_in_erp";

    // A supplier suspended only while the ERP approves it is reactivated by that approval, and no longer will be once
    // the ERP disables it instead.
    public const string ReactivatedApprovedInErp = "supplier.reactivated_approved_in_erp";
    public const string ErpReleaseWithdrawn = "supplier.erp_release_withdrawn";

    // The push of a supplier that registered here into the ERP, written by SupplierErpPushJob with the system as the
    // actor. ErpPushCreated is the ERP holding the Supplier record and the portal holding its name as ExternalId, whether
    // this attempt created it or found the one an earlier attempt made. ErpPushCompleted is its address, contact and
    // website user being there too. ErpPushAttemptFailed is one attempt that will be tried again, and ErpPushFailed is
    // the push stopping until a person retries it; each carries what went wrong as its reason.
    public const string ErpPushCreated = "supplier.erp_push_created";
    public const string ErpPushCompleted = "supplier.erp_push_completed";
    public const string ErpPushAttemptFailed = "supplier.erp_push_attempt_failed";
    public const string ErpPushFailed = "supplier.erp_push_failed";
}
