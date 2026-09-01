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
    /// <summary>Task #7/Stage C: create/list Organizations, manage OrgUnits, and the manual
    /// SupplierOrgLink create/remove action (BRULE-010) - a distinct admin surface from
    /// AdminUsersManage, following that constant's own pattern of being system_admin-only by
    /// default (via Permissions.All below) rather than assigned to any other role.</summary>
    public const string AdminOrganizationsManage = "admin.organizations.manage";
    /// <summary>FR-ADM-002: edit a role's permission set. Distinct from AdminUsersManage (which
    /// covers who has an account/which role they hold) - this covers what a role itself grants.
    /// Only SystemAdmin holds it by default (via Permissions.All below); ManageRolesHandler
    /// additionally refuses any update that would leave zero roles holding it, so this permission
    /// can never be edited into a state where nobody can ever edit roles again.</summary>
    public const string AdminRolesManage = "admin.roles.manage";
    /// <summary>FEAT-06.3/FR-OFF-004/FR-SRCH-001: procurement staff searching offerings for RFQ
    /// invitation candidates - distinct from SupplierEdit (which is the owning supplier managing
    /// its own catalog) since this is a different actor reading across all suppliers.</summary>
    public const string OfferingSearch = "offering.search";

    /// <summary>FEAT-11.1/FR-ADM-005, pulled forward for EPIC-07: manage EvaluationTemplates
    /// (criteria, weights, thresholds, activate/archive/fork). Not prefixed "admin." - like
    /// rfq.publish/evaluation.score/award.approve, this is a domain-owned procurement permission
    /// (procurement_manager-held), not a system-catalog admin permission.</summary>
    public const string EvaluationTemplateManage = "evaluation.template.manage";

    /// <summary>FEAT-07.1/FR-RFQ-001: create a Draft RFQ, scoped to the actor's own
    /// OrganizationId (BRULE-029).</summary>
    public const string RfqCreate = "rfq.create";
    /// <summary>FEAT-07.1..07.3: routine content edits (items, requirements, attachments,
    /// evaluation-template binding) while Draft - distinct from RfqCreate since a delegate could
    /// plausibly edit an RFQ they didn't create, and distinct from the state-transition
    /// permissions below per this catalog's own established pattern (e.g. SupplierReview vs
    /// SupplierApprove).</summary>
    public const string RfqEdit = "rfq.edit";
    /// <summary>FEAT-07.4/BUSINESS-PROCESSES.md §3.1: Draft -> InternalReview.</summary>
    public const string RfqSubmitReview = "rfq.submit_review";
    /// <summary>FEAT-07.4/BUSINESS-PROCESSES.md §3.1: InternalReview -> Draft ("return for
    /// edits") - distinct from RfqApprove per that same transition table naming a separate
    /// `rfq.review` permission for the return path.</summary>
    public const string RfqReview = "rfq.review";
    /// <summary>FEAT-07.4/BUSINESS-PROCESSES.md §3.1: InternalReview -> Approved.</summary>
    public const string RfqApprove = "rfq.approve";
    /// <summary>FEAT-07.6/BUSINESS-PROCESSES.md §3.1: SubmissionOpen -> SubmissionClosed,
    /// manual early close with reason (the scheduled deadline-driven close is a system actor and
    /// carries no permission check).</summary>
    public const string RfqClose = "rfq.close";
    /// <summary>FEAT-07.8/BUSINESS-PROCESSES.md §3.1: cancel from any pre-Awarded state, reason
    /// mandatory.</summary>
    public const string RfqCancel = "rfq.cancel";
    /// <summary>FEAT-08.1/FR-INV-001: invite a supplier (and view FEAT-08.2 candidate suggestions)
    /// - distinct from RfqEdit per this catalog's established pattern of a separate permission per
    /// named actor/action pair in BUSINESS-PROCESSES.md, even though both are procurement_officer
    /// today.</summary>
    public const string RfqInvite = "rfq.invite";
    /// <summary>FEAT-10.2/FR-CLR-002: answer a clarification question, privately or published -
    /// procurement_officer per that FR's own actor.</summary>
    public const string ClarificationAnswer = "clarification.answer";
    /// <summary>FEAT-10.4/FR-CLR-004/FR-RFQ-012: issue an RFQ addendum - the "locked after
    /// Published except addenda" carve-out, distinct from RfqEdit since it is legal only Published+
    /// where RfqEdit is legal only Draft.</summary>
    public const string RfqAddendum = "rfq.addendum";

    public static readonly IReadOnlyList<string> All =
    [
        SupplierEdit, SupplierSubmit, SupplierApprove, SupplierReview, SupplierReject, SupplierRequestInfo, DocumentReview,
        SupplierBankAccountManage, SupplierUserManage, SupplierLifecycleManage,
        RfqPublish, ProposalSubmit, EvaluationScore, AwardApprove, AdminUsersManage, AuditRead, AdminOrganizationsManage,
        AdminRolesManage, OfferingSearch, EvaluationTemplateManage,
        RfqCreate, RfqEdit, RfqSubmitReview, RfqReview, RfqApprove, RfqClose, RfqCancel, RfqInvite,
        ClarificationAnswer, RfqAddendum
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
        // BUSINESS-PROCESSES.md §3.1: procurement_officer authors, submits for review, publishes,
        // and may close-early; procurement_manager reviews/approves/cancels. FEAT-11.1: template
        // management is procurement_manager/system_admin per BACKLOG.md's own actor list.
        [ProcurementOfficer] = [Permissions.RfqPublish, Permissions.OfferingSearch, Permissions.RfqCreate, Permissions.RfqEdit, Permissions.RfqSubmitReview, Permissions.RfqClose, Permissions.RfqInvite, Permissions.ClarificationAnswer, Permissions.RfqAddendum],
        // FR-ONB-009 names onboarding_reviewer, procurement_manager and system_admin as the
        // three roles permitted to move a supplier's post-approval lifecycle.
        [ProcurementManager] = [Permissions.RfqPublish, Permissions.AwardApprove, Permissions.SupplierLifecycleManage, Permissions.OfferingSearch, Permissions.RfqReview, Permissions.RfqApprove, Permissions.RfqCancel, Permissions.EvaluationTemplateManage],
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
