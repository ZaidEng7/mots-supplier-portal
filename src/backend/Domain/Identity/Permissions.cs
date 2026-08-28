namespace MotsSupplierPortal.Domain.Identity;

/// <summary>
/// Canonical resource.action permission catalog (docs/architecture/00-foundational-decisions.md §6).
/// Roles are named permission sets seeded per persona; the API enforces these independent of the UI.
/// </summary>
public static class Permissions
{
    public const string SupplierEdit = "supplier.edit";
    public const string SupplierSubmit = "supplier.submit";
    public const string SupplierApprove = "supplier.approve";
    /// <summary>Pick up an application and review it (STORY-03.2.1) - distinct from
    /// SupplierApprove per product-owner decision 2026-08-26, so approval and the
    /// review workflow can be granted independently if a role ever needs one without
    /// the other.</summary>
    public const string SupplierReview = "supplier.review";
    /// <summary>Reject an application - distinct from SupplierReview per
    /// docs/product/PERSONAS.md's onboarding_reviewer permission list (2026-08-27 naming-drift
    /// fix), so reject can be granted independently of pickup/review if a role ever needs it.</summary>
    public const string SupplierReject = "supplier.reject";
    public const string SupplierRequestInfo = "supplier.requestInfo";
    /// <summary>Document-level approve/reject (FEAT-05.4) - simpler than, and distinct from, the
    /// application-level three-way decision above.</summary>
    public const string DocumentReview = "supplier.document.review";
    /// <summary>FEAT-04.6/MSP-53: bank accounts are the most sensitive profile field (BRULE-014/
    /// 090/091) - scoped tighter than general SupplierEdit, supplier_admin only, not
    /// supplier_user.</summary>
    public const string SupplierBankAccountManage = "supplier.bankAccount.manage";
    /// <summary>FEAT-04.8/MSP-55: supplier_admin only, not supplier_user - a delegated user must
    /// not be able to invite/disable other delegated users.</summary>
    public const string SupplierUserManage = "supplier.user.manage";
    /// <summary>FR-ONB-009 (MSP-63): post-approval lifecycle - suspend, reactivate, deactivate.
    /// Distinct from SupplierApprove because approving an application and suspending a live
    /// supplier are different authorities: the first admits, the second removes an operating
    /// supplier from all future selection, and deactivation is irreversible.</summary>
    public const string SupplierLifecycleManage = "supplier.lifecycle.manage";
    public const string RfqPublish = "rfq.publish";
    public const string ProposalSubmit = "proposal.submit";
    public const string EvaluationScore = "evaluation.score";
    public const string AwardApprove = "award.approve";
    public const string AdminUsersManage = "admin.users.manage";
    public const string AuditRead = "audit.read";

    public static readonly IReadOnlyList<string> All =
    [
        SupplierEdit, SupplierSubmit, SupplierApprove, SupplierReview, SupplierReject, SupplierRequestInfo, DocumentReview,
        SupplierBankAccountManage, SupplierUserManage, SupplierLifecycleManage,
        RfqPublish, ProposalSubmit, EvaluationScore, AwardApprove, AdminUsersManage, AuditRead
    ];
}

/// <summary>Canonical persona role names (docs/product/PERSONAS.md).</summary>
public static class Roles
{
    public const string SupplierAdmin = "supplier_admin";
    public const string SupplierUser = "supplier_user";
    public const string OnboardingReviewer = "onboarding_reviewer";
    public const string ProcurementOfficer = "procurement_officer";
    public const string ProcurementManager = "procurement_manager";
    public const string Evaluator = "evaluator";
    public const string MinistryViewer = "ministry_viewer";
    public const string SystemAdmin = "system_admin";

    /// <summary>Default permission set per persona at seed time (admin-editable thereafter).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> DefaultPermissions = new Dictionary<string, string[]>
    {
        [SupplierAdmin] = [Permissions.ProposalSubmit, Permissions.SupplierEdit, Permissions.SupplierSubmit, Permissions.SupplierBankAccountManage, Permissions.SupplierUserManage],
        [SupplierUser] = [Permissions.ProposalSubmit, Permissions.SupplierEdit],
        [OnboardingReviewer] = [Permissions.SupplierApprove, Permissions.SupplierReview, Permissions.SupplierReject, Permissions.SupplierRequestInfo, Permissions.DocumentReview, Permissions.SupplierLifecycleManage],
        [ProcurementOfficer] = [Permissions.RfqPublish],
        // FR-ONB-009 names onboarding_reviewer, procurement_manager and system_admin as the
        // three roles permitted to move a supplier's post-approval lifecycle.
        [ProcurementManager] = [Permissions.RfqPublish, Permissions.AwardApprove, Permissions.SupplierLifecycleManage],
        [Evaluator] = [Permissions.EvaluationScore],
        // MSP-62 (2026-08-28): audit.read REMOVED from ministry_viewer. BRULE-086 grants the
        // Ministry "read-only, cross-organization access to aggregate/governance metrics only",
        // and BRULE-087 defaults to aggregate-only where visibility is undecided. A raw audit-row
        // read is neither aggregate nor a metric - it exposes named actors (ActorLabel) and
        // reviewer free text (Reason) at line level for every supplier, which is the RISK-007
        // exposure. The Ministry's legitimate governance view belongs to EPIC-18/EPIC-19, which
        // are unbuilt; granting raw audit access as an interim stand-in grants strictly more than
        // BRULE-086 allows. Re-add only if OQ-001 resolves in favour of line-level Ministry access.
        [MinistryViewer] = [],
        [SystemAdmin] = [.. Permissions.All],
    };
}
