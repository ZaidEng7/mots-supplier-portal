// Every application handler this API can resolve, grouped by the feature it belongs to.
//
// These are all the same kind of registration: an interface the endpoints ask for, and the one class
// that implements it. They live here rather than in the start-up file because there are well over two
// hundred of them, and a reader looking for the tender handlers should not have to scroll past the
// notification ones to find them.
//
// They are written out one by one rather than discovered by scanning the assembly. A scan removes the
// lines and takes the answer with them: with a scan, "what does this application actually register?"
// can only be answered by running it, and a mis-named class fails at run time instead of at compile
// time. The cost of writing them out is length, which grouping solves; the cost of scanning is
// knowing, which nothing solves.
//
// Nothing here depends on the order. Every service type in this file is registered exactly once, so
// the grouping is free to follow the product rather than the history of who added what when.
//
// AddApplicationHandlers calls each group in turn. Adding a feature means adding a method and one
// line to that list.
//
// The groups, and what each one covers:
//
//   ReferenceData        the reference lists every other feature points at by code
//   Offerings            a supplier's catalogue, and the buyer-side search across all of them
//   SupplierProfile      a supplier's own profile and the people, places and accounts attached to it
//   Documents            uploading, reading and deciding on compliance documents
//   Review               the onboarding reviewer's queue, decisions and post-approval lifecycle
//   Tenders              authoring a tender and moving it through its life
//   Invitations          inviting suppliers to a tender
//   Proposals            a supplier's bid
//   EvaluationTemplates  the scoring templates a tender binds to
//   Evaluation           running an evaluation and scoring bids
//   Comparison           the comparison matrix
//   Awards               recommending, approving and issuing an award
//   Workspace            the guided workspace read model
//   Reports              the procurement and compliance reports
//   Dashboards           the four dashboards
//   Governance           the ministry's cross-organization reads
//   Organizations        buying organizations and their department trees
//   Staff                staff accounts and what each role grants
//   Identity             signing in, tokens, passwords, the account screen and sessions
//   Notifications        the notification bell and its preferences
//   Email                sending mail, and the admin-editable templates
//   Administration       the administrator and operator screens
//   Platform             the adapters underneath everything: storage, scanning, audit, request context

namespace MotsSupplierPortal.Api.Startup;

using System.Reflection;
using MotsSupplierPortal.Infrastructure.Dashboards;
using MotsSupplierPortal.Application.Dashboards;
using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Application.Notifications;
using MotsSupplierPortal.Api.Errors;
using MotsSupplierPortal.Api.Concurrency;
using System.IO.Compression;
using System.Threading.RateLimiting;
using FluentValidation;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Api.Endpoints;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Identity;
using MotsSupplierPortal.Application.Organizations;
using MotsSupplierPortal.Application.Registrations;
using MotsSupplierPortal.Application.ReferenceData;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Infrastructure.ReferenceData;
using MotsSupplierPortal.Application.Evaluation;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Application.Comparison;
using MotsSupplierPortal.Application.Reports;
using MotsSupplierPortal.Application.Awards;
using MotsSupplierPortal.Application.Workspace;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Audit;
using MotsSupplierPortal.Infrastructure.Auth;
using MotsSupplierPortal.Infrastructure.Evaluation;
using MotsSupplierPortal.Infrastructure.Identity;
using MotsSupplierPortal.Infrastructure.Organizations;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Registrations;
using MotsSupplierPortal.Infrastructure.Rfqs;
using MotsSupplierPortal.Infrastructure.Proposals;
using MotsSupplierPortal.Infrastructure.Comparison;
using MotsSupplierPortal.Infrastructure.Reports;
using MotsSupplierPortal.Infrastructure.Awards;
using MotsSupplierPortal.Infrastructure.Workspace;
using MotsSupplierPortal.Infrastructure.Storage;
using MotsSupplierPortal.Infrastructure.Suppliers;
using Microsoft.AspNetCore.ResponseCompression;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

internal static class ApplicationHandlerRegistration
{
    internal static WebApplicationBuilder AddApplicationHandlers(this WebApplicationBuilder builder)
    {
        builder.AddReferenceDataHandlers();
        builder.AddOfferingsHandlers();
        builder.AddSupplierProfileHandlers();
        builder.AddDocumentsHandlers();
        builder.AddReviewHandlers();
        builder.AddTendersHandlers();
        builder.AddInvitationsHandlers();
        builder.AddProposalsHandlers();
        builder.AddEvaluationTemplatesHandlers();
        builder.AddEvaluationHandlers();
        builder.AddComparisonHandlers();
        builder.AddAwardsHandlers();
        builder.AddWorkspaceHandlers();
        builder.AddReportsHandlers();
        builder.AddDashboardsHandlers();
        builder.AddGovernanceHandlers();
        builder.AddOrganizationsHandlers();
        builder.AddStaffHandlers();
        builder.AddIdentityHandlers();
        builder.AddNotificationsHandlers();
        builder.AddEmailHandlers();
        builder.AddAdministrationHandlers();
        builder.AddPlatformHandlers();

        return builder;
    }

    private static void AddReferenceDataHandlers(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IGetCurrenciesHandler, GetCurrenciesHandler>();
        builder.Services.AddScoped<IGetRegionsHandler, GetRegionsHandler>();
        builder.Services.AddScoped<IGetCategoriesHandler, GetCategoriesHandler>();
        builder.Services.AddScoped<IGetUnitsOfMeasureHandler, GetUnitsOfMeasureHandler>();
        builder.Services.AddScoped<IGetIncotermsHandler, GetIncotermsHandler>();
        builder.Services.AddScoped<IReferenceDataAdminHandler, ReferenceDataAdminHandler>();
        builder.Services.AddScoped<MotsSupplierPortal.Application.ReferenceData.IGetDocumentTypeCategoriesHandler, MotsSupplierPortal.Infrastructure.ReferenceData.GetDocumentTypeCategoriesHandler>();
        builder.Services.AddScoped<MotsSupplierPortal.Application.ReferenceData.ISetDocumentTypeCategoriesHandler, MotsSupplierPortal.Infrastructure.ReferenceData.SetDocumentTypeCategoriesHandler>();
    }

    private static void AddOfferingsHandlers(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IListOfferingsHandler, ListOfferingsHandler>();
        builder.Services.AddScoped<ICreateOfferingHandler, CreateOfferingHandler>();
        builder.Services.AddScoped<IUpdateOfferingHandler, UpdateOfferingHandler>();
        builder.Services.AddScoped<IDeactivateOfferingHandler, DeactivateOfferingHandler>();
        builder.Services.AddScoped<ISearchBuyerOfferingsHandler, SearchBuyerOfferingsHandler>();
        builder.Services.AddScoped<IGetOfferingHandler, GetOfferingHandler>();
    }

    private static void AddSupplierProfileHandlers(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<MotsSupplierPortal.Infrastructure.Security.FieldEncryptionService>();
        builder.Services.AddScoped<MotsSupplierPortal.Infrastructure.Idempotency.IdempotencyCleanupJob>();
        builder.Services.AddScoped<IUpdateLegalInfoHandler, UpdateLegalInfoHandler>();
        builder.Services.AddScoped<IUploadLogoHandler, UploadLogoHandler>();
        builder.Services.AddScoped<IGetLogoDownloadUrlHandler, GetLogoDownloadUrlHandler>();
        builder.Services.AddScoped<IManageRepresentativeHandler, ManageRepresentativeHandler>();
        builder.Services.AddScoped<IManageAddressHandler, ManageAddressHandler>();
        builder.Services.AddScoped<IManageContactHandler, ManageContactHandler>();
        builder.Services.AddScoped<IManageBranchHandler, ManageBranchHandler>();
        builder.Services.AddScoped<IManageSupplierOrgLinkHandler, ManageSupplierOrgLinkHandler>();
        builder.Services.AddScoped<IManageCategoryLinkHandler, ManageCategoryLinkHandler>();
        builder.Services.AddScoped<IInviteSupplierUserHandler, InviteSupplierUserHandler>();
        builder.Services.AddScoped<IListSupplierUsersHandler, ListSupplierUsersHandler>();
        builder.Services.AddScoped<IDisableSupplierUserHandler, DisableSupplierUserHandler>();
        builder.Services.AddScoped<IAcceptSupplierUserInviteHandler, AcceptSupplierUserInviteHandler>();
        builder.Services.AddScoped<IGetSupplierHandler, GetSupplierHandler>();
        builder.Services.AddScoped<ISupplierRegistryExportHandler, SupplierRegistryExportHandler>();
        builder.Services.AddScoped<IMinistrySupplierFeedHandler, MinistrySupplierFeedHandler>();
        builder.Services.AddScoped<IMinistryRfqFeedHandler, MinistryRfqFeedHandler>();
        builder.Services.AddScoped<IUpdateProfileHandler, UpdateProfileHandler>();
        builder.Services.AddScoped<IAcceptTermsHandler, AcceptTermsHandler>();
        builder.Services.AddScoped<ISubmitApplicationHandler, SubmitApplicationHandler>();
        builder.Services.AddScoped<ISupplierCodeScope, SupplierCodeScope>();
        builder.Services.AddScoped<IListSupplierDirectoryHandler, MotsSupplierPortal.Infrastructure.Suppliers.ListSupplierDirectoryHandler>();
    }

    private static void AddDocumentsHandlers(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IManageProposalDocumentHandler, ManageProposalDocumentHandler>();
        builder.Services.AddScoped<IGetOwnProposalDocumentDownloadUrlHandler, GetOwnProposalDocumentDownloadUrlHandler>();
        builder.Services.AddScoped<IGetProposalDocumentsForBuyerHandler, GetProposalDocumentsForBuyerHandler>();
        builder.Services.AddScoped<IGetProposalDocumentDownloadUrlForBuyerHandler, GetProposalDocumentDownloadUrlForBuyerHandler>();
        builder.Services.AddScoped<IGetSupplierDocumentHandler, GetSupplierDocumentHandler>();
        builder.Services.AddScoped<IGetDocumentHistoryHandler, GetDocumentHistoryHandler>();
        builder.Services.AddScoped<IListSupplierDocumentsHandler, ListSupplierDocumentsHandler>();
        builder.Services.AddScoped<IListSupplierDocumentsPagedHandler, ListSupplierDocumentsPagedHandler>();
        builder.Services.AddScoped<IUploadDocumentHandler, UploadDocumentHandler>();
        builder.Services.AddScoped<IGetDocumentDownloadUrlHandler, GetDocumentDownloadUrlHandler>();
        builder.Services.AddScoped<IApproveDocumentHandler, ApproveDocumentHandler>();
        builder.Services.AddScoped<IRejectDocumentHandler, RejectDocumentHandler>();
        builder.Services.AddScoped<DocumentScanJob>();
        builder.Services.AddScoped<DocumentExpiryJob>();
    }

    private static void AddReviewHandlers(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IListReviewQueueHandler, ListReviewQueueHandler>();
        builder.Services.AddScoped<IListComplianceDirectoryHandler, MotsSupplierPortal.Infrastructure.Suppliers.ListComplianceDirectoryHandler>();
        builder.Services.AddScoped<IClaimReviewItemHandler, ClaimReviewItemHandler>();
        builder.Services.AddScoped<IUnassignReviewItemHandler, UnassignReviewItemHandler>();
        builder.Services.AddScoped<IGetReviewerSupplierViewHandler, GetReviewerSupplierViewHandler>();
        builder.Services.AddScoped<IPickUpApplicationHandler, PickUpApplicationHandler>();
        builder.Services.AddScoped<IApproveApplicationHandler, ApproveApplicationHandler>();
        builder.Services.AddScoped<IRejectApplicationHandler, RejectApplicationHandler>();
        builder.Services.AddScoped<ISupplierLifecycleHandler, SupplierLifecycleHandler>();
        builder.Services.AddScoped<IRequestInfoHandler, RequestInfoHandler>();
        builder.Services.AddScoped<IResubmitApplicationHandler, ResubmitApplicationHandler>();
        builder.Services.AddScoped<IGetOwnActiveAnnotationHandler, GetOwnActiveAnnotationHandler>();
    }

    private static void AddTendersHandlers(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IListRfqsHandler, ListRfqsHandler>();
        builder.Services.AddScoped<IGetRfqHandler, GetRfqHandler>();
        builder.Services.AddScoped<ICreateRfqHandler, CreateRfqHandler>();
        builder.Services.AddScoped<IUpdateRfqBasicsHandler, UpdateRfqBasicsHandler>();
        builder.Services.AddScoped<IChangeSubmissionDeadlineHandler, ChangeSubmissionDeadlineHandler>();
        builder.Services.AddScoped<IManageRfqItemHandler, ManageRfqItemHandler>();
        builder.Services.AddScoped<IManageRequirementHandler, ManageRequirementHandler>();
        builder.Services.AddScoped<IManageRfqAttachmentHandler, ManageRfqAttachmentHandler>();
        builder.Services.AddScoped<ISubmitRfqForReviewHandler, SubmitRfqForReviewHandler>();
        builder.Services.AddScoped<IReassignRfqHandler, ReassignRfqHandler>();
        builder.Services.AddScoped<IListRfqAssigneesHandler, ListRfqAssigneesHandler>();
        builder.Services.AddScoped<IReturnRfqForEditsHandler, ReturnRfqForEditsHandler>();
        builder.Services.AddScoped<IApproveRfqHandler, ApproveRfqHandler>();
        builder.Services.AddScoped<IPublishRfqHandler, PublishRfqHandler>();
        builder.Services.AddScoped<IRequestRfqClarificationHandler, RequestRfqClarificationHandler>();
        builder.Services.AddScoped<IResolveRfqClarificationHandler, ResolveRfqClarificationHandler>();
        builder.Services.AddScoped<ICloseRfqSubmissionHandler, CloseRfqSubmissionHandler>();
        builder.Services.AddScoped<ICancelRfqHandler, CancelRfqHandler>();
        builder.Services.AddScoped<RfqTimelineJob>();
        builder.Services.AddScoped<ISupplierListInvitedRfqsHandler, SupplierListInvitedRfqsHandler>();
        builder.Services.AddScoped<ISupplierGetRfqHandler, SupplierGetRfqHandler>();
        builder.Services.AddScoped<IAnswerClarificationHandler, AnswerClarificationHandler>();
        builder.Services.AddScoped<IPublishClarificationHandler, PublishClarificationHandler>();
        builder.Services.AddScoped<IIssueAddendumHandler, IssueAddendumHandler>();
        builder.Services.AddScoped<ISupplierPostClarificationHandler, SupplierPostClarificationHandler>();
        builder.Services.AddScoped<IAnswerRequirementHandler, AnswerRequirementHandler>();
        builder.Services.AddScoped<IGetRfqAttachmentDownloadUrlHandler, GetRfqAttachmentDownloadUrlHandler>();
    }

    private static void AddInvitationsHandlers(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IInviteSupplierHandler, InviteSupplierHandler>();
        builder.Services.AddScoped<ISuggestInvitationCandidatesHandler, SuggestInvitationCandidatesHandler>();
        builder.Services.AddScoped<ISupplierDeclineInvitationHandler, SupplierDeclineInvitationHandler>();
    }

    private static void AddProposalsHandlers(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IListBuyerProposalsHandler, ListBuyerProposalsHandler>();
        builder.Services.AddScoped<IGetBuyerProposalHandler, GetBuyerProposalHandler>();
        builder.Services.AddScoped<IListMyProposalsHandler, ListMyProposalsHandler>();
        builder.Services.AddScoped<IStartProposalHandler, StartProposalHandler>();
        builder.Services.AddScoped<IGetProposalHandler, GetProposalHandler>();
        builder.Services.AddScoped<IGetProposalByCodeHandler, GetProposalByCodeHandler>();
        builder.Services.AddScoped<IManageProposalItemHandler, ManageProposalItemHandler>();
        builder.Services.AddScoped<ISetCommercialTermsHandler, SetCommercialTermsHandler>();
        builder.Services.AddScoped<ISetNarrativeHandler, SetNarrativeHandler>();
        builder.Services.AddScoped<IPatchProposalHandler, PatchProposalHandler>();
        builder.Services.AddScoped<ISubmitProposalHandler, SubmitProposalHandler>();
        builder.Services.AddScoped<IWithdrawProposalHandler, WithdrawProposalHandler>();
        builder.Services.AddScoped<IRequestProposalClarificationHandler, RequestProposalClarificationHandler>();
        builder.Services.AddScoped<IReviseProposalHandler, ReviseProposalHandler>();
    }

    private static void AddEvaluationTemplatesHandlers(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IListEvaluationTemplatesHandler, ListEvaluationTemplatesHandler>();
        builder.Services.AddScoped<IGetEvaluationTemplateHandler, GetEvaluationTemplateHandler>();
        builder.Services.AddScoped<ICreateEvaluationTemplateHandler, CreateEvaluationTemplateHandler>();
        builder.Services.AddScoped<IManageCriterionHandler, ManageCriterionHandler>();
        builder.Services.AddScoped<IActivateEvaluationTemplateHandler, ActivateEvaluationTemplateHandler>();
        builder.Services.AddScoped<IArchiveEvaluationTemplateHandler, ArchiveEvaluationTemplateHandler>();
        builder.Services.AddScoped<IForkEvaluationTemplateHandler, ForkEvaluationTemplateHandler>();
        builder.Services.AddScoped<IBindEvaluationTemplateHandler, BindEvaluationTemplateHandler>();
    }

    private static void AddEvaluationHandlers(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IOpenEvaluationHandler, OpenEvaluationHandler>();
        builder.Services.AddScoped<IGetEvaluationHandler, GetEvaluationHandler>();
        builder.Services.AddScoped<IAssignEvaluatorsHandler, AssignEvaluatorsHandler>();
        builder.Services.AddScoped<IRecuseEvaluatorHandler, RecuseEvaluatorHandler>();
        builder.Services.AddScoped<IConsolidateEvaluationHandler, ConsolidateEvaluationHandler>();
        builder.Services.AddScoped<IResolveEvaluationTieHandler, ResolveEvaluationTieHandler>();
        builder.Services.AddScoped<IGetConflictDeclarationHandler, GetConflictDeclarationHandler>();
        builder.Services.AddScoped<IDeclareConflictHandler, DeclareConflictHandler>();
        builder.Services.AddScoped<IGetProposalDocumentDownloadUrlForEvaluatorHandler, GetProposalDocumentDownloadUrlForEvaluatorHandler>();
        builder.Services.AddScoped<IFinalizeEvaluationHandler, FinalizeEvaluationHandler>();
        builder.Services.AddScoped<IReopenEvaluationHandler, ReopenEvaluationHandler>();
        builder.Services.AddScoped<IGetMyEvaluationHandler, GetMyEvaluationHandler>();
        builder.Services.AddScoped<IScoreCriterionHandler, ScoreCriterionHandler>();
        builder.Services.AddScoped<ISubmitEvaluatorHandler, SubmitEvaluatorHandler>();
        builder.Services.AddScoped<MotsSupplierPortal.Application.Evaluation.IListEvaluatorCandidatesHandler, MotsSupplierPortal.Infrastructure.Evaluation.ListEvaluatorCandidatesHandler>();
    }

    private static void AddComparisonHandlers(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IGetComparisonHandler, GetComparisonHandler>();
    }

    private static void AddAwardsHandlers(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IDeclineAwardOfferHandler, DeclineAwardOfferHandler>();
        builder.Services.AddScoped<IGetAwardHandler, GetAwardHandler>();
        builder.Services.AddScoped<IRecommendAwardHandler, RecommendAwardHandler>();
        builder.Services.AddScoped<IRouteAwardForApprovalHandler, RouteAwardForApprovalHandler>();
        builder.Services.AddScoped<IApproveAwardHandler, ApproveAwardHandler>();
        builder.Services.AddScoped<IRejectAwardHandler, RejectAwardHandler>();
        builder.Services.AddScoped<IExecuteAwardHandler, ExecuteAwardHandler>();
        builder.Services.AddScoped<IRetryErpSyncHandler, RetryErpSyncHandler>();
        builder.Services.AddScoped<AwardErpSyncJob>();
        builder.Services.AddScoped<MotsSupplierPortal.Application.Governance.IGetMinistryAwardAnalyticsHandler, MotsSupplierPortal.Infrastructure.Governance.GetMinistryAwardAnalyticsHandler>();
    }

    private static void AddWorkspaceHandlers(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IGetWorkspaceHandler, GetWorkspaceHandler>();
    }

    private static void AddReportsHandlers(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<MotsSupplierPortal.Infrastructure.Exports.ReportFonts>();
        builder.Services.AddScoped<IProcurementReportHandler, ProcurementReportHandler>();
        builder.Services.AddScoped<IComplianceReportHandler, ComplianceReportHandler>();
    }

    private static void AddDashboardsHandlers(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IListMyAssignmentsHandler, ListMyAssignmentsHandler>();
        builder.Services.AddScoped<IProcurementDashboardHandler, ProcurementDashboardHandler>();
        builder.Services.AddScoped<IApprovalQueuesHandler, ApprovalQueuesHandler>();
        builder.Services.AddScoped<IReviewDashboardHandler, ReviewDashboardHandler>();
        builder.Services.AddScoped<ISupplierDashboardHandler, SupplierDashboardHandler>();
    }

    private static void AddGovernanceHandlers(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<MotsSupplierPortal.Application.Governance.IGetGovernanceOverviewHandler, MotsSupplierPortal.Infrastructure.Governance.GetGovernanceOverviewHandler>();
        builder.Services.AddScoped<MotsSupplierPortal.Application.Governance.IGetCategoryCoverageHandler, MotsSupplierPortal.Infrastructure.Governance.GetCategoryCoverageHandler>();
        builder.Services.AddScoped<MotsSupplierPortal.Application.Governance.IListMinistryRfqsHandler, MotsSupplierPortal.Infrastructure.Governance.ListMinistryRfqsHandler>();
        builder.Services.AddScoped<MotsSupplierPortal.Application.Governance.IListMinistrySuppliersHandler, MotsSupplierPortal.Infrastructure.Governance.ListMinistrySuppliersHandler>();
        builder.Services.AddScoped<MotsSupplierPortal.Application.Governance.IGetMinistryRfqDetailHandler, MotsSupplierPortal.Infrastructure.Governance.GetMinistryRfqDetailHandler>();
    }

    private static void AddOrganizationsHandlers(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<ICreateOrganizationHandler, CreateOrganizationHandler>();
        builder.Services.AddScoped<IListOrganizationsHandler, ListOrganizationsHandler>();
        builder.Services.AddScoped<IManageOrgUnitHandler, ManageOrgUnitHandler>();
    }

    private static void AddStaffHandlers(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IInviteStaffHandler, InviteStaffHandler>();
        builder.Services.AddScoped<IListStaffHandler, ListStaffHandler>();
        builder.Services.AddScoped<ISetStaffActiveHandler, SetStaffActiveHandler>();
        builder.Services.AddScoped<IChangeStaffRoleHandler, ChangeStaffRoleHandler>();
        builder.Services.AddScoped<IResetStaffMfaHandler, ResetStaffMfaHandler>();
        builder.Services.AddScoped<IAcceptStaffInviteHandler, AcceptStaffInviteHandler>();
        builder.Services.AddScoped<IListRolesHandler, ListRolesHandler>();
        builder.Services.AddScoped<IUpdateRolePermissionsHandler, UpdateRolePermissionsHandler>();
    }

    private static void AddIdentityHandlers(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IManageBankAccountHandler, ManageBankAccountHandler>();
        builder.Services.AddScoped<ISecurityTokenService, SecurityTokenService>();
        builder.Services.AddScoped<IRegisterSupplierHandler, RegisterSupplierHandler>();
        builder.Services.AddScoped<IVerifyEmailHandler, VerifyEmailHandler>();
        builder.Services.AddScoped<IResendVerificationHandler, ResendVerificationHandler>();
        builder.Services.AddScoped<DraftCleanupJob>();
        builder.Services.AddSingleton<MotsSupplierPortal.Api.Authorization.PerTargetRateLimiter>();
        builder.Services.AddScoped<IIdentityProvider, AspNetIdentityProvider>();
        builder.Services.AddScoped<LoginHandler>();
        builder.Services.AddScoped<ILoginHandler>(sp => sp.GetRequiredService<LoginHandler>());
        builder.Services.AddScoped<IRefreshTokenHandler, RefreshTokenHandler>();
        builder.Services.AddScoped<IForgotPasswordHandler, ForgotPasswordHandler>();
        builder.Services.AddScoped<IResetPasswordHandler, ResetPasswordHandler>();
        builder.Services.AddScoped<IChangePasswordHandler, ChangePasswordHandler>();
        builder.Services.AddScoped<IGetAccountHandler, GetAccountHandler>();
        builder.Services.AddScoped<IUpdateAccountHandler, UpdateAccountHandler>();
        builder.Services.AddScoped<IChooseLanguageHandler, ChooseLanguageHandler>();
        builder.Services.AddScoped<IEnrollMfaHandler, EnrollMfaHandler>();
        builder.Services.AddScoped<IConfirmMfaEnrollmentHandler, ConfirmMfaEnrollmentHandler>();
        builder.Services.AddScoped<IListSessionsHandler, ListSessionsHandler>();
        builder.Services.AddScoped<IRevokeSessionHandler, RevokeSessionHandler>();
        builder.Services.AddScoped<IRevokeAllSessionsHandler, RevokeAllSessionsHandler>();
        builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
        builder.Services.AddScoped<PermissionResolver>();
    }

    private static void AddNotificationsHandlers(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<MotsSupplierPortal.Application.Notifications.INotificationCopySource, MotsSupplierPortal.Infrastructure.Notifications.NotificationCopySource>();
        builder.Services.AddScoped<MotsSupplierPortal.Application.Notifications.INotificationTemplateAdminHandler, MotsSupplierPortal.Infrastructure.Notifications.NotificationTemplateAdminHandler>();
        builder.Services.AddScoped<IListNotificationsHandler, ListNotificationsHandler>();
        builder.Services.AddScoped<IUnreadNotificationCountHandler, UnreadNotificationCountHandler>();
        builder.Services.AddScoped<IMarkNotificationReadHandler, MarkNotificationReadHandler>();
        builder.Services.AddScoped<IGetNotificationPreferencesHandler, MotsSupplierPortal.Infrastructure.Notifications.GetNotificationPreferencesHandler>();
        builder.Services.AddScoped<ISetNotificationPreferencesHandler, MotsSupplierPortal.Infrastructure.Notifications.SetNotificationPreferencesHandler>();
        builder.Services.AddScoped<MotsSupplierPortal.Application.Notifications.INotificationMaterialiser, MotsSupplierPortal.Infrastructure.Notifications.NotificationMaterialiser>();
    }

    private static void AddEmailHandlers(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<MotsSupplierPortal.Application.Admin.IEmailCopySource, MotsSupplierPortal.Infrastructure.Email.EmailCopySource>();
        builder.Services.AddScoped<MotsSupplierPortal.Application.Admin.IListEmailTemplatesHandler, MotsSupplierPortal.Infrastructure.Email.ListEmailTemplatesHandler>();
        builder.Services.AddScoped<MotsSupplierPortal.Application.Admin.IUpsertEmailTemplateHandler, MotsSupplierPortal.Infrastructure.Email.UpsertEmailTemplateHandler>();
        builder.Services.AddScoped<MotsSupplierPortal.Application.Admin.IDeleteEmailTemplateHandler, MotsSupplierPortal.Infrastructure.Email.DeleteEmailTemplateHandler>();
        builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.SectionName));
        builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
        builder.Services.AddScoped<EmailJobs>();
    }

    private static void AddAdministrationHandlers(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<MotsSupplierPortal.Application.Admin.IGetAdminOverviewHandler, MotsSupplierPortal.Infrastructure.Admin.GetAdminOverviewHandler>();
        builder.Services.AddScoped<IGetFieldConfigHandler, GetFieldConfigHandler>();
        builder.Services.AddScoped<IGetOneFieldConfigHandler, GetOneFieldConfigHandler>();
        builder.Services.AddScoped<IUpdateFieldConfigHandler, UpdateFieldConfigHandler>();
        builder.Services.AddScoped<MotsSupplierPortal.Infrastructure.Configuration.ISystemSettingReader, MotsSupplierPortal.Infrastructure.Configuration.SystemSettingReader>();
        builder.Services.AddScoped<MotsSupplierPortal.Application.Configuration.ISystemSettingAdminHandler, MotsSupplierPortal.Infrastructure.Configuration.SystemSettingAdminHandler>();
        builder.Services.AddScoped<MotsSupplierPortal.Application.Admin.IGetJobsMonitorHandler, MotsSupplierPortal.Infrastructure.Admin.GetJobsMonitorHandler>();
        builder.Services.AddScoped<MotsSupplierPortal.Application.Admin.ITriggerRecurringJobHandler, MotsSupplierPortal.Infrastructure.Admin.TriggerRecurringJobHandler>();
        builder.Services.AddScoped<MotsSupplierPortal.Application.Admin.IGetOutboxMonitorHandler, MotsSupplierPortal.Infrastructure.Admin.GetOutboxMonitorHandler>();
        builder.Services.AddScoped<MotsSupplierPortal.Application.Admin.IReplayOutboxMessageHandler, MotsSupplierPortal.Infrastructure.Admin.ReplayOutboxMessageHandler>();
        builder.Services.AddScoped<MotsSupplierPortal.Application.Admin.IGetErpSyncMonitorHandler, MotsSupplierPortal.Infrastructure.Admin.GetErpSyncMonitorHandler>();
        builder.Services.AddScoped<MotsSupplierPortal.Application.Admin.IGetSecurityPostureHandler, MotsSupplierPortal.Infrastructure.Admin.SecurityPostureHandler>();
        builder.Services.AddScoped<MotsSupplierPortal.Application.Search.ISearchHandler, MotsSupplierPortal.Infrastructure.Search.SearchHandler>();
        builder.Services.AddScoped<MotsSupplierPortal.Application.Admin.IGetStorageSettingsHandler, MotsSupplierPortal.Infrastructure.Admin.StorageSettingsHandler>();
        builder.Services.AddScoped<MotsSupplierPortal.Application.Admin.IGetUiStringBundleHandler, MotsSupplierPortal.Infrastructure.Admin.GetUiStringBundleHandler>();
        builder.Services.AddScoped<MotsSupplierPortal.Application.Admin.IListUiStringOverridesHandler, MotsSupplierPortal.Infrastructure.Admin.ListUiStringOverridesHandler>();
        builder.Services.AddScoped<MotsSupplierPortal.Application.Admin.IUpsertUiStringOverrideHandler, MotsSupplierPortal.Infrastructure.Admin.UpsertUiStringOverrideHandler>();
        builder.Services.AddScoped<MotsSupplierPortal.Application.Admin.IDeleteUiStringOverrideHandler, MotsSupplierPortal.Infrastructure.Admin.DeleteUiStringOverrideHandler>();
        builder.Services.AddScoped<MotsSupplierPortal.Application.Platform.ISystemStatusHandler, MotsSupplierPortal.Infrastructure.Platform.SystemStatusHandler>();
    }

    private static void AddPlatformHandlers(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IErpPurchaseOrderAdapter, StubErpPurchaseOrderAdapter>();
        builder.Services.AddSingleton<IOutboxTransport, MotsSupplierPortal.Infrastructure.Suppliers.LoggingOutboxTransport>();
        builder.Services.AddScoped<MotsSupplierPortal.Infrastructure.Suppliers.OutboxDispatcher>();
        builder.Services.AddSingleton<MotsSupplierPortal.Infrastructure.Suppliers.OutboxBacklogGauge>();
        builder.Services.Configure<MinioOptions>(builder.Configuration.GetSection(MinioOptions.SectionName));
        builder.Services.AddSingleton<MinioFileStorage>();
        builder.Services.AddScoped<IFileStorage>(sp => sp.GetRequiredService<MinioFileStorage>());
        builder.Services.Configure<ClamAvOptions>(builder.Configuration.GetSection(ClamAvOptions.SectionName));
        builder.Services.AddScoped<IVirusScanner, ClamAvScanner>();
        builder.Services.AddScoped<AttachmentScanner>();
        builder.Services.AddScoped<MotsSupplierPortal.Application.Audit.IGetAuditLogHandler, MotsSupplierPortal.Infrastructure.Audit.GetAuditLogHandler>();
        builder.Services.AddScoped<IAuditLogger, AuditLogger>();
        builder.Services.AddScoped<IScopeContext, HttpScopeContext>();
        builder.Services.AddScoped<IConcurrencyContext, HttpConcurrencyContext>();
        builder.Services.AddScoped<IAuditContext, MotsSupplierPortal.Api.Authorization.HttpAuditContext>();
        builder.Services.AddValidatorsFromAssemblyContaining<RegisterSupplierRequestValidator>();
    }
}
