namespace MotsSupplierPortal.Domain.Identity;

/// <summary>
/// Canonical resource.action permission catalog (docs/architecture/00-foundational-decisions.md §6).
/// Roles are named permission sets seeded per persona; the API enforces these independent of the UI.
/// </summary>
public static class Permissions
{
    public const string SupplierApprove = "supplier.approve";
    public const string RfqPublish = "rfq.publish";
    public const string ProposalSubmit = "proposal.submit";
    public const string EvaluationScore = "evaluation.score";
    public const string AwardApprove = "award.approve";
    public const string AdminUsersManage = "admin.users.manage";
    public const string AuditRead = "audit.read";

    public static readonly IReadOnlyList<string> All =
    [
        SupplierApprove, RfqPublish, ProposalSubmit, EvaluationScore, AwardApprove, AdminUsersManage, AuditRead
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
        [SupplierAdmin] = [Permissions.ProposalSubmit],
        [SupplierUser] = [Permissions.ProposalSubmit],
        [OnboardingReviewer] = [Permissions.SupplierApprove],
        [ProcurementOfficer] = [Permissions.RfqPublish],
        [ProcurementManager] = [Permissions.RfqPublish, Permissions.AwardApprove],
        [Evaluator] = [Permissions.EvaluationScore],
        [MinistryViewer] = [Permissions.AuditRead],
        [SystemAdmin] = [.. Permissions.All],
    };
}
