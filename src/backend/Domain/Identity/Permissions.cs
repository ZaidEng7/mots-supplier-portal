// Every permission in the system, and what each role holds when the database is first seeded.
//
// A permission is named resource.action. Roles are named sets of them. The API checks permissions
// itself, independently of what the interface chooses to show, so hiding a button is never the
// thing that stops an action.
//
// The catalogue is deliberately fine-grained: one permission per actor-and-action pair named in the
// written process tables, even where the same role holds both today. Reviewing, approving and
// rejecting an application are three permissions, not one, so a role can be given one without the
// others. The cost of splitting is a longer list; the cost of merging is that widening one role
// silently widens another.
//
// Adding a permission here does not grant it anywhere. An environment seeded before a permission
// existed needs it granted by hand, which is why reading tenders, requesting clarification and
// reading reports are on the first-deploy checklist.
//
//
// THE SUPPLIER-FACING PERMISSIONS
//
// SupplierEdit and SupplierSubmit are a company maintaining and submitting its own application.
//
// SupplierBankAccountManage is scoped tighter than SupplierEdit and held by a supplier's own
// administrator only, because the bank account is the most sensitive field on the profile.
//
// SupplierUserManage is the supplier's administrator only, so a delegated user cannot invite or
// disable other delegated users.
//
//
// THE REVIEWER-FACING PERMISSIONS
//
// SupplierReview is picking up an application and working it. SupplierApprove and SupplierReject are
// the two ways it ends, and SupplierRequestInfo is the third outcome that sends it back. All four are
// separate so a role can be given the review workflow without the authority to decide, or the
// authority to reject without the authority to admit.
//
// DocumentReview is approving or rejecting one document, which is a simpler decision than the
// three-way one on the application as a whole.
//
// SupplierLifecycleManage is suspending, reactivating and deactivating a supplier after approval. It
// is not SupplierApprove, because those are different authorities: approval admits a company, while
// suspension removes a working one from every future selection, and deactivation cannot be undone.
//
// SupplierDirectoryRead is browsing the registry of companies before inviting anyone: names,
// categories and whether a company can currently trade. It is not OfferingSearch, which is the same
// person asking a different question, searching catalogue entries rather than listing companies.
// Reusing the offering permission would have meant a name about offerings gating a directory of
// suppliers, and nobody reading a role list could tell which screens it opened. It is not
// SupplierReview either, because the reviewer's version of that directory carries each company's
// document health, and a buying officer has no business reading a supplier's document history.
//
//
// THE TENDER PERMISSIONS
//
// RfqRead is reading a tender: the list, the detail, and the guided workspace. It was split out of
// RfqCreate, which those reads used to be gated on. Reading is not authoring, and conflating the two
// broke approval outright: the manager is the actor who approves a tender, and that role holds
// review, approve and cancel but deliberately not create, so a manager required to approve a tender
// could not list or open one. The fix was a read permission rather than widening the manager's
// grant, because giving approvers authoring rights would weaken exactly the separation of duties the
// award flow exists to enforce. Row scoping still does the real work: every handler behind these
// routes filters to the caller's own organization, so this permission decides who may ask, not what
// comes back.
//
// RfqCreate is starting a draft, scoped to the actor's own organization.
//
// RfqEdit is routine content work while the tender is still a draft: items, requirements,
// attachments, the evaluation template. Separate from RfqCreate because a delegate could plausibly
// edit a tender they did not create.
//
// RfqSubmitReview, RfqReview and RfqApprove are the three steps of internal review. Review is named
// separately because it covers the return path, sending a tender back for edits.
//
// RfqPublish opens a tender to suppliers. RfqClose is a manual early close with a reason; the
// scheduled close at the deadline is the system acting and carries no permission check at all.
//
// RfqCancel is cancelling from any state before award, with a mandatory reason.
//
// RfqInvite is inviting a supplier, and seeing the suggested candidates.
//
// RfqReassign hands a tender to another officer. It is new and appears in no document, because
// ownership did not exist to be moved. It is deliberately not the officer's own: the point of
// ownership is putting one named person on record as responsible, and an owner who may reassign
// their own tender away can drop that responsibility without anybody deciding they should. It
// belongs to the manager, so an officer who cannot continue has to ask, and the audit row records
// what was asked and why.
//
// RfqAddendum is issuing an addendum. It is the one carve-out in "locked once published", and it is
// separate from RfqEdit because it is legal only after publishing where RfqEdit is legal only in
// draft.
//
// RfqDeadlineShorten is moving a submission window earlier. The name is an invention, since the rule
// names an actor and no permission, but the policy is not: extending a deadline is the officer's
// under RfqEdit, and shortening one is the manager's. It is its own constant rather than a reuse of
// some other manager-only permission, because overloading approval would mean everyone with
// approval authority silently gained the power to cut a live tender short.
//
// ClarificationAnswer and RfqClarify share a word and nothing else. ClarificationAnswer is answering
// a supplier's question during the submission window, privately or published. RfqClarify is the
// evaluation-stage clarification transitions, and it is held by the evaluator as well as the officer.
//
//
// THE BID PERMISSIONS
//
// ProposalCreate and ProposalEdit are held by both supplier roles: starting a bid and working on it
// while it is a draft.
//
// ProposalSubmit, ProposalWithdraw, ProposalRevise and ProposalDecline are the supplier's
// administrator only. Declining an award is a commitment of the same weight as making one.
//
//
// THE EVALUATION PERMISSIONS
//
// EvaluationOpen starts evaluation once the window has closed. EvaluationAssign builds the committee,
// and also covers recusing an evaluator, which is the same roster authority. EvaluationScore and
// EvaluationSubmit are the evaluator's own work. EvaluationConsolidate gathers the scores,
// EvaluationFinalize settles them, and EvaluationReopen sends them back with a mandatory reason.
//
// EvaluationTemplateManage is the criteria, weights and thresholds, with activate, archive and fork.
// It is not prefixed admin, because like publishing a tender or scoring a bid it is a procurement
// authority the manager holds, not a system-catalogue one.
//
// ComparisonView is the comparison matrix. It is its own permission rather than a reuse of
// RfqCreate, which the officer alone holds, or EvaluationConsolidate, which is an action rather than
// a view.
//
//
// THE AWARD PERMISSIONS
//
// AwardRecommend names a winner, and also covers re-recommending after a rejection. AwardApprove and
// AwardReject are the approver's two answers, separate from each other for the same reason review
// and approve are separate on a tender.
//
// IntegrationRetry clears a failed finance sync.
//
//
// THE ADMINISTRATIVE PERMISSIONS
//
// AdminUsersManage covers who has an account and which role they hold.
//
// AdminRolesManage covers what a role itself grants, which is a different question. Only the system
// administrator holds it, and the handler refuses any change that would leave no role holding it, so
// roles can never be edited into a state where nobody can ever edit roles again.
//
// AdminOrganizationsManage covers creating and listing buying organizations, their department tree,
// and the manual link between a supplier and an organization. It is a different admin surface from
// managing users, and it is the system administrator's by default.
//
// ReferenceDataManage covers the category tree, document types, currencies, units, delivery terms
// and regions. Its own permission rather than a reuse of user management, because reference data
// decides what every supplier in the country may register against.
//
// AuditRead is reading the compliance record.
//
//
// THE CROSS-ORGANIZATION PERMISSIONS
//
// GovernanceRead is the ministry's read-only view across every buying organization, and it covers
// aggregate governance figures only. It is not RfqRead or ReportRead: both of those are scoped to one
// organization, so a cross-organization read borrowing either would see nothing or quietly bypass the
// scoping that makes them safe.
//
// ReportRead is cross-organization aggregate reporting. It was granted to no role at all, which left
// the reports screen reachable by nobody. It is now the manager's, who already holds approval
// authority over the work these reports aggregate and so learns nothing they could not reach case by
// case, and the ministry viewer's. Not the officer's, who has no cross-organization remit.
//
//
// THE ROLES
//
// Eight personas, and DefaultPermissions is what each one holds at seed time. An administrator can
// change any of it afterwards.
//
// Both supplier roles hold RfqRead because the tender list is one collection serving both sides of
// the product, gated by permission and filtered by row scope. A supplier reading the tenders they
// were invited to is still reading a tender, and the invitation-scoped handler behind it is
// unchanged, so this widens who may call the route rather than what any caller can see.
//
// The officer authors, submits for review, publishes, invites, and may close early. The manager
// reviews, approves and cancels. Post-approval supplier lifecycle belongs to the onboarding
// reviewer, the manager and the system administrator.
//
// The evaluator holds RfqClarify because the process table names the evaluator alongside the officer
// as someone who can request clarification.
//
// The ministry viewer once held an empty set, which meant the persona could sign in and reach
// nothing at all. It now holds governance and reports.
//
// Audit reading was removed from the ministry viewer deliberately. The ministry's access is to
// aggregate figures across organizations, and a raw audit read is neither aggregate nor a figure: it
// exposes named actors and reviewers' free text, line by line, for every supplier. That is the
// disclosure risk the register names. Restore it only if the open question resolves in favour of
// line-level ministry access.
//
// SupplierRegistryExport is the whole registry as one file: every supplier at every onboarding state, with
// tax identifiers, named people, their email addresses and phone numbers, and bank account holders. It is
// deliberately NOT SupplierDirectoryRead, which is browsing companies before inviting one and is held by both
// procurement roles - a screen a person reads one supplier at a time is a different disclosure from a file
// that leaves the building with all of them in it. It is not granted to the ministry viewer either, for the
// reason audit reading was taken away from that persona: the ministry's access is to aggregate figures, and a
// registry export is line-level personal data, which is exactly the risk the register names. Only the system
// administrator holds it, through All.
//
// The system administrator holds everything in the catalogue.

namespace MotsSupplierPortal.Domain.Identity;

public static class Permissions
{
    public const string SupplierEdit = "supplier.edit";
    public const string SupplierSubmit = "supplier.submit";
    public const string SupplierApprove = "supplier.approve";
    public const string SupplierReview = "supplier.review";
    public const string SupplierReject = "supplier.reject";
    public const string SupplierRequestInfo = "supplier.requestInfo";
    public const string DocumentReview = "supplier.document.review";
    public const string SupplierBankAccountManage = "supplier.bankAccount.manage";
    public const string SupplierUserManage = "supplier.user.manage";
    public const string SupplierLifecycleManage = "supplier.lifecycle.manage";
    public const string SupplierDirectoryRead = "supplier.directory.read";
    public const string SupplierRegistryExport = "supplier.registry.export";

    public const string RfqRead = "rfq.read";
    public const string RfqCreate = "rfq.create";
    public const string RfqEdit = "rfq.edit";
    public const string RfqSubmitReview = "rfq.submit_review";
    public const string RfqReview = "rfq.review";
    public const string RfqApprove = "rfq.approve";
    public const string RfqPublish = "rfq.publish";
    public const string RfqClose = "rfq.close";
    public const string RfqCancel = "rfq.cancel";
    public const string RfqInvite = "rfq.invite";
    public const string RfqReassign = "rfq.reassign";
    public const string RfqAddendum = "rfq.addendum";
    public const string RfqDeadlineShorten = "rfq.deadline.shorten";
    public const string RfqClarify = "rfq.clarify";
    public const string ClarificationAnswer = "clarification.answer";

    public const string OfferingSearch = "offering.search";

    public const string ProposalCreate = "proposal.create";
    public const string ProposalEdit = "proposal.edit";
    public const string ProposalSubmit = "proposal.submit";
    public const string ProposalWithdraw = "proposal.withdraw";
    public const string ProposalRevise = "proposal.revise";
    public const string ProposalDecline = "proposal.decline";

    public const string EvaluationTemplateManage = "evaluation.template.manage";
    public const string EvaluationOpen = "evaluation.open";
    public const string EvaluationAssign = "evaluation.assign";
    public const string EvaluationScore = "evaluation.score";
    public const string EvaluationSubmit = "evaluation.submit";
    public const string EvaluationConsolidate = "evaluation.consolidate";
    public const string EvaluationFinalize = "evaluation.finalize";
    public const string EvaluationReopen = "evaluation.reopen";
    public const string ComparisonView = "comparison.view";

    public const string AwardRecommend = "award.recommend";
    public const string AwardApprove = "award.approve";
    public const string AwardReject = "award.reject";
    public const string IntegrationRetry = "integration.retry";

    public const string AdminUsersManage = "admin.users.manage";
    public const string AdminRolesManage = "admin.roles.manage";
    public const string AdminOrganizationsManage = "admin.organizations.manage";
    public const string ReferenceDataManage = "reference.manage";
    public const string AuditRead = "audit.read";

    public const string GovernanceRead = "governance.read";
    public const string ReportRead = "report.read";

    public static readonly IReadOnlyList<string> All =
    [
        SupplierEdit, SupplierSubmit, SupplierApprove, SupplierReview, SupplierReject, SupplierRequestInfo, DocumentReview,
        SupplierBankAccountManage, SupplierUserManage, SupplierLifecycleManage,
        RfqPublish, ProposalSubmit, EvaluationScore, AwardApprove, AdminUsersManage, AuditRead, AdminOrganizationsManage,
        AdminRolesManage, OfferingSearch, EvaluationTemplateManage,
        RfqRead, RfqCreate, RfqEdit, RfqSubmitReview, RfqReview, RfqApprove, RfqClose, RfqCancel, RfqInvite,
        ClarificationAnswer, RfqClarify, RfqAddendum, ProposalCreate, ProposalEdit, ProposalWithdraw,
        EvaluationOpen, EvaluationAssign, EvaluationSubmit, EvaluationConsolidate, EvaluationFinalize, EvaluationReopen,
        ComparisonView, AwardReject, AwardRecommend, IntegrationRetry, ReportRead, ProposalRevise, ProposalDecline,
        RfqDeadlineShorten, ReferenceDataManage, GovernanceRead, RfqReassign, SupplierDirectoryRead,
        SupplierRegistryExport
    ];
}

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

    public static readonly IReadOnlyDictionary<string, string[]> DefaultPermissions = new Dictionary<string, string[]>
    {
        [SupplierAdmin] = [Permissions.RfqRead, Permissions.ProposalCreate, Permissions.ProposalEdit, Permissions.ProposalSubmit, Permissions.ProposalWithdraw, Permissions.ProposalDecline, Permissions.ProposalRevise, Permissions.SupplierEdit, Permissions.SupplierSubmit, Permissions.SupplierBankAccountManage, Permissions.SupplierUserManage],
        [SupplierUser] = [Permissions.RfqRead, Permissions.ProposalCreate, Permissions.ProposalEdit, Permissions.SupplierEdit],
        [OnboardingReviewer] = [Permissions.SupplierApprove, Permissions.SupplierReview, Permissions.SupplierReject, Permissions.SupplierRequestInfo, Permissions.DocumentReview, Permissions.SupplierLifecycleManage],
        [ProcurementOfficer] = [Permissions.RfqPublish, Permissions.OfferingSearch, Permissions.SupplierDirectoryRead, Permissions.RfqRead, Permissions.RfqCreate, Permissions.RfqEdit, Permissions.RfqSubmitReview, Permissions.RfqClose, Permissions.RfqInvite, Permissions.ClarificationAnswer, Permissions.RfqClarify, Permissions.RfqAddendum, Permissions.EvaluationOpen, Permissions.EvaluationConsolidate, Permissions.ComparisonView, Permissions.AwardRecommend],
        [ProcurementManager] = [Permissions.ReportRead, Permissions.RfqRead, Permissions.RfqPublish, Permissions.AwardApprove, Permissions.SupplierLifecycleManage, Permissions.OfferingSearch, Permissions.SupplierDirectoryRead, Permissions.RfqReview, Permissions.RfqApprove, Permissions.RfqCancel, Permissions.EvaluationTemplateManage, Permissions.EvaluationOpen, Permissions.EvaluationAssign, Permissions.EvaluationConsolidate, Permissions.EvaluationFinalize, Permissions.EvaluationReopen, Permissions.ComparisonView, Permissions.AwardRecommend, Permissions.AwardReject, Permissions.RfqDeadlineShorten, Permissions.RfqReassign],
        [Evaluator] = [Permissions.EvaluationScore, Permissions.EvaluationSubmit, Permissions.RfqClarify],
        [MinistryViewer] = [Permissions.GovernanceRead, Permissions.ReportRead],
        [SystemAdmin] = [.. Permissions.All],
    };
}
