# Permission catalogue

**Generated. Do not edit.** Produced by `PermissionCatalogueTests` from `Permissions.All`,
`Roles.DefaultPermissions` and the `RequirePermission` call sites; the test fails when this file
drifts from the code. Regenerate with `UPDATE_PERMISSION_CATALOGUE=1 dotnet test`.

Every name here is an **invention against codebase convention** — no document in `docs/`
ratifies a `resource.action` string. That is the point of this file: A-16 asks for one pass in
which the whole set can be ratified or renamed, rather than each name staying provisional
forever. A permission held by NO role is reachable by nobody, and one gating NO route is
either dead or waiting for a surface — both are called out below.

51 permissions, 8 roles.

| Permission | Held by default | Gates |
|---|---|---|
| `admin.organizations.manage` | `system_admin` | `AddOrgUnit`, `CreateOrganization`, `CreateSupplierOrgLink`, `ListOrganizations`, `ListSupplierOrgLinks`, `RemoveOrgUnit`, `RemoveSupplierOrgLink` |
| `admin.roles.manage` | `system_admin` | `ListRoles`, `UpdateRolePermissions` |
| `admin.users.manage` | `system_admin` | `GetAdminOverview`, `GetFieldConfig`, `GetOneFieldConfig`, `InviteStaff`, `UpdateFieldConfig` |
| `audit.read` | `system_admin` | `ExportAuditLog`, `GetAuditLog`, `SearchAuditLog` |
| `award.approve` | `procurement_manager`, `system_admin` | `ApproveAward`, `ExecuteAward`, checked in ProcurementDashboardHandler, not on a route |
| `award.recommend` | `procurement_manager`, `procurement_officer`, `system_admin` | `GetAward`, `RecommendAward`, `RouteAwardForApproval` |
| `award.reject` | `procurement_manager`, `system_admin` | `RejectAward` |
| `clarification.answer` | `procurement_officer`, `system_admin` | `AnswerClarification`, `PublishClarification` |
| `comparison.view` | `procurement_manager`, `procurement_officer`, `system_admin` | `ExportComparison`, `GetComparison`, `GetProposalDocumentDownloadUrlForBuyer`, `GetProposalDocumentsForBuyer` |
| `evaluation.assign` | `procurement_manager`, `system_admin` | `AssignEvaluators`, `RecuseEvaluator` |
| `evaluation.consolidate` | `procurement_manager`, `procurement_officer`, `system_admin` | `ConsolidateEvaluation`, `ResolveEvaluationTie` |
| `evaluation.finalize` | `procurement_manager`, `system_admin` | `FinalizeEvaluation` |
| `evaluation.open` | `procurement_manager`, `procurement_officer`, `system_admin` | `GetEvaluation`, `OpenEvaluation` |
| `evaluation.reopen` | `procurement_manager`, `system_admin` | `ReopenEvaluation` |
| `evaluation.score` | `evaluator`, `system_admin` | `DeclareConflict`, `GetConflictDeclaration`, `GetMyEvaluation`, `GetProposalDocumentDownloadUrlForEvaluator`, `ListMyAssignments`, `ScoreCriterion` |
| `evaluation.submit` | `evaluator`, `system_admin` | `SubmitEvaluatorScores` |
| `evaluation.template.manage` | `procurement_manager`, `system_admin` | `ActivateEvaluationTemplate`, `AddCriterion`, `ArchiveEvaluationTemplate`, `CreateEvaluationTemplate`, `ForkEvaluationTemplate`, `GetEvaluationTemplate`, `ListEvaluationTemplates`, `RemoveCriterion`, `UpdateCriterion` |
| `governance.read` | `ministry_viewer`, `system_admin` | `GetGovernanceOverview` |
| `integration.retry` | `system_admin` | `RetryAwardErpSync` |
| `offering.search` | `procurement_manager`, `procurement_officer`, `system_admin` | `SearchBuyerOfferings` |
| `proposal.create` | `supplier_admin`, `supplier_user`, `system_admin` | `GetProposal`, `GetProposalByCode`, `StartProposal`, `SupplierDeclineInvitation`, `SupplierPostClarification` |
| `proposal.decline` | `supplier_admin`, `system_admin` | `DeclineAwardOffer` |
| `proposal.edit` | `supplier_admin`, `supplier_user`, `system_admin` | `AddProposalDocument`, `GetOwnProposalDocumentDownloadUrl`, `PatchProposal`, `RemoveProposalDocument` |
| `proposal.revise` | `system_admin` | `ReviseProposal` |
| `proposal.submit` | `supplier_admin`, `system_admin` | `SubmitProposal` |
| `proposal.withdraw` | `supplier_admin`, `system_admin` | `WithdrawProposal` |
| `reference.manage` | `system_admin` | `CreateReferenceItem`, `DeactivateReferenceItem`, `ListNotificationTemplates`, `ListReferenceItems`, `ListSystemSettings`, `ReactivateReferenceItem`, `RevertNotificationTemplate`, `UpdateNotificationTemplate`, `UpdateReferenceItem`, `UpdateSystemSetting` |
| `report.read` | `procurement_manager`, `system_admin` | `ExportComplianceReport`, `ExportProcurementReport`, `GetComplianceReport`, `GetProcurementReport` |
| `rfq.addendum` | `procurement_officer`, `system_admin` | `IssueAddendum` |
| `rfq.approve` | `procurement_manager`, `system_admin` | `ApprovalQueues`, `ApproveRfq`, checked in ProcurementDashboardHandler, not on a route |
| `rfq.cancel` | `procurement_manager`, `system_admin` | `CancelRfq` |
| `rfq.clarify` | `evaluator`, `procurement_officer`, `system_admin` | `ResolveRfqClarification` |
| `rfq.close` | `procurement_officer`, `system_admin` | `CloseRfqSubmission` |
| `rfq.create` | `procurement_officer`, `system_admin` | `CreateRfq` |
| `rfq.deadline.shorten` | `procurement_manager`, `system_admin` | checked in RfqHandlers, not on a route |
| `rfq.edit` | `procurement_officer`, `system_admin` | `AddRequirement`, `AddRfqAttachment`, `AddRfqItem`, `BindEvaluationTemplate`, `RemoveRequirement`, `RemoveRfqAttachment`, `RemoveRfqItem`, `UpdateRfqBasics` |
| `rfq.invite` | `procurement_officer`, `system_admin` | `InviteSupplier`, `SuggestInvitationCandidates` |
| `rfq.publish` | `procurement_manager`, `procurement_officer`, `system_admin` | `PublishRfq` |
| `rfq.read` | `procurement_manager`, `procurement_officer`, `supplier_admin`, `supplier_user`, `system_admin` | `GetRfq`, `GetRfqAttachmentDownloadUrl`, `GetWorkspace`, `ListRfqs`, `ProcurementDashboard` |
| `rfq.review` | `procurement_manager`, `system_admin` | `ReturnRfqForEdits` |
| `rfq.submit_review` | `procurement_officer`, `system_admin` | `SubmitRfqForReview` |
| `supplier.approve` | `onboarding_reviewer`, `system_admin` | `ApproveApplication` |
| `supplier.bankAccount.manage` | `supplier_admin`, `system_admin` | `AddBankAccount`, `RemoveBankAccount`, `RevealBankAccount`, `SetDefaultBankAccount`, `UpdateBankAccount` |
| `supplier.document.review` | `onboarding_reviewer`, `system_admin` | `ApproveDocument`, `RejectDocument`, checked in GetDocumentDownloadUrlHandler, not on a route, checked in GetSupplierDocumentHandler, not on a route |
| `supplier.edit` | `supplier_admin`, `supplier_user`, `system_admin` | `AcceptTerms`, `AddAddress`, `AddBranch`, `AddContact`, `AddRepresentative`, `CreateOffering`, `DeactivateOffering`, `GetOffering`, `LinkCategory`, `ListOfferings`, `RemoveAddress`, `RemoveBranch`, `RemoveContact`, `RemoveRepresentative`, `ResubmitApplication`, `SetPrimaryRepresentative`, `UnlinkCategory`, `UpdateAddress`, `UpdateBranch`, `UpdateContact`, `UpdateLegalInfo`, `UpdateOffering`, `UpdateRepresentative`, `UpdateSupplierProfile`, `UploadDocument`, `UploadLogo` |
| `supplier.lifecycle.manage` | `onboarding_reviewer`, `procurement_manager`, `system_admin` | `ReviewEndpoints (name resolved at runtime)` |
| `supplier.reject` | `onboarding_reviewer`, `system_admin` | `RejectApplication` |
| `supplier.requestInfo` | `onboarding_reviewer`, `system_admin` | `RequestApplicationInfo` |
| `supplier.review` | `onboarding_reviewer`, `system_admin` | `ClaimReviewItem`, `GetReviewerSupplierView`, `ListReviewQueue`, `PickUpApplication`, `ReviewDashboard`, `UnassignReviewItem` |
| `supplier.submit` | `supplier_admin`, `system_admin` | `SubmitSupplierApplication` |
| `supplier.user.manage` | `supplier_admin`, `system_admin` | `DisableSupplierUser`, `InviteSupplierUser`, `ListSupplierUsers` |

## Roles

| Role | Permissions held by default |
|---|---|
| `evaluator` | `evaluation.score`, `evaluation.submit`, `rfq.clarify` |
| `ministry_viewer` | `governance.read` |
| `onboarding_reviewer` | `supplier.approve`, `supplier.document.review`, `supplier.lifecycle.manage`, `supplier.reject`, `supplier.requestInfo`, `supplier.review` |
| `procurement_manager` | `award.approve`, `award.recommend`, `award.reject`, `comparison.view`, `evaluation.assign`, `evaluation.consolidate`, `evaluation.finalize`, `evaluation.open`, `evaluation.reopen`, `evaluation.template.manage`, `offering.search`, `report.read`, `rfq.approve`, `rfq.cancel`, `rfq.deadline.shorten`, `rfq.publish`, `rfq.read`, `rfq.review`, `supplier.lifecycle.manage` |
| `procurement_officer` | `award.recommend`, `clarification.answer`, `comparison.view`, `evaluation.consolidate`, `evaluation.open`, `offering.search`, `rfq.addendum`, `rfq.clarify`, `rfq.close`, `rfq.create`, `rfq.edit`, `rfq.invite`, `rfq.publish`, `rfq.read`, `rfq.submit_review` |
| `supplier_admin` | `proposal.create`, `proposal.decline`, `proposal.edit`, `proposal.submit`, `proposal.withdraw`, `rfq.read`, `supplier.bankAccount.manage`, `supplier.edit`, `supplier.submit`, `supplier.user.manage` |
| `supplier_user` | `proposal.create`, `proposal.edit`, `rfq.read`, `supplier.edit` |
| `system_admin` | `admin.organizations.manage`, `admin.roles.manage`, `admin.users.manage`, `audit.read`, `award.approve`, `award.recommend`, `award.reject`, `clarification.answer`, `comparison.view`, `evaluation.assign`, `evaluation.consolidate`, `evaluation.finalize`, `evaluation.open`, `evaluation.reopen`, `evaluation.score`, `evaluation.submit`, `evaluation.template.manage`, `governance.read`, `integration.retry`, `offering.search`, `proposal.create`, `proposal.decline`, `proposal.edit`, `proposal.revise`, `proposal.submit`, `proposal.withdraw`, `reference.manage`, `report.read`, `rfq.addendum`, `rfq.approve`, `rfq.cancel`, `rfq.clarify`, `rfq.close`, `rfq.create`, `rfq.deadline.shorten`, `rfq.edit`, `rfq.invite`, `rfq.publish`, `rfq.read`, `rfq.review`, `rfq.submit_review`, `supplier.approve`, `supplier.bankAccount.manage`, `supplier.document.review`, `supplier.edit`, `supplier.lifecycle.manage`, `supplier.reject`, `supplier.requestInfo`, `supplier.review`, `supplier.submit`, `supplier.user.manage` |
