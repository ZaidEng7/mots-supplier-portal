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
using MotsSupplierPortal.Application.Reference;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Application.Evaluation;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Application.Evaluations;
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
using MotsSupplierPortal.Infrastructure.Reference;
using MotsSupplierPortal.Infrastructure.Registrations;
using MotsSupplierPortal.Infrastructure.Rfqs;
using MotsSupplierPortal.Infrastructure.Proposals;
using MotsSupplierPortal.Infrastructure.Evaluations;
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

var builder = WebApplication.CreateBuilder(args);

// Fail fast before any service reads a setting: a misconfigured non-Development deployment
// must not boot. See RequiredConfiguration for why (three settings used to degrade silently).
MotsSupplierPortal.Api.Configuration.RequiredConfiguration.Validate(builder.Configuration, builder.Environment);

// Serilog: structured JSON logging (docs/architecture/OBSERVABILITY-ARCHITECTURE.md).
// RedactingEnricher is the NFR-PRIV-004 redaction stage (MSP-61) - it must stay registered
// before the sink so no deny-listed property value can reach the console/aggregator.
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .Enrich.With(new MotsSupplierPortal.Infrastructure.Observability.RedactingEnricher())
    .Enrich.WithProperty("Application", "MotsSupplierPortal.Api")
    .WriteTo.Console(new Serilog.Formatting.Json.JsonFormatter()));

// Task #16/NFR-OBS-006: metrics, alongside the traces this project already had. Zero Meter/Counter
// instrumentation existed anywhere before this (grepped the whole backend). ASP.NET Core's own
// built-in meters (wired below) give request count/latency/status-code PER ROUTE for free, for
// every endpoint - not the hand-picked "high-traffic ones" this ticket named as a fallback,
// because the free version is strictly more complete and costs nothing extra to wire up.
// AppMetrics is the small amount that instrumentation cannot see (see its own doc comment).
// PrometheusExporter:
// the only OTel .NET exporter for a pull-based /metrics endpoint, carries a "beta" NuGet version
// (1.18.0-beta.1, matching the otherwise-1.18.0 OTel packages already here) - the whole Prometheus
// exporter family for .NET OTel has stayed beta-versioned upstream for a long time despite wide
// production use; evaluated and accepted rather than defaulting to it unexamined.
builder.Services.AddSingleton<MotsSupplierPortal.Infrastructure.Observability.AppMetrics>();
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("MotsSupplierPortal.Api"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        // Built-in meters, not a NuGet instrumentation package - unlike tracing,
        // OpenTelemetry.Instrumentation.AspNetCore's AddAspNetCoreInstrumentation() only targets
        // TracerProviderBuilder (confirmed by trying it here first and getting a compile error).
        // ASP.NET Core has emitted per-request metrics (http.server.request.duration,
        // http.server.active_requests, tagged by route/method/status) natively via
        // System.Diagnostics.Metrics since .NET 8 - these two meter names are the framework's own,
        // per Microsoft's ASP.NET Core metrics docs. Microsoft.AspNetCore.Identity is the other
        // free one worth having: sign-in/sign-out/password-check counters for an app whose entire
        // surface is behind Identity-based auth.
        .AddMeter("Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Server.Kestrel", "Microsoft.AspNetCore.Identity")
        .AddMeter(MotsSupplierPortal.Infrastructure.Observability.AppMetrics.MeterName)
        .AddPrometheusExporter());

builder.Services.AddOpenApi();

// Global: every enum (AddressKind, OnboardingState, SupplierLegalType, DocumentTypeKind, ...)
// reads/writes its string name on the wire, not a raw integer - applies to every Minimal API
// endpoint's request/response JSON binding, not just one handler.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());

    // NFR-SEC-005: reject payloads carrying fields we do not model, rather than silently ignoring
    // them. System.Text.Json's default is to skip unknown members, which meant a typo'd or stale
    // field name was swallowed and the caller told "200 OK" - a client could believe it had
    // updated something it had not (found in review 2026-08-28). Applied globally so every
    // endpoint inherits it, not just the one where it was noticed.
    options.SerializerOptions.UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow;
});

// Outside Development RequiredConfiguration has already guaranteed this is present, so the only
// remaining fallback is the local-dev credential (NFR-SEC-007: dev-only, and weak on purpose).
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Host=localhost;Port=5432;Database=mots_supplier_portal;Username=postgres;Password=postgres";

// The interceptor is resolved per scope so it can see the current request's If-Match header; see
// ExpectedVersionInterceptor for why this is not a SaveChangesAsync override.
builder.Services.AddScoped<ExpectedVersionInterceptor>();
builder.Services.AddDbContext<AppDbContext>((sp, options) => options
    .UseNpgsql(connectionString)
    .AddInterceptors(sp.GetRequiredService<ExpectedVersionInterceptor>()));

// Hangfire: durable background jobs (verification email, password-reset email) backed by Postgres
// so a queued send survives an app restart (docs/architecture/00-foundational-decisions.md §2).
// MSP-98: the schema is configuration rather than a constant. A hardcoded schema is a testability
// problem in its own right - it makes "this host's Hangfire storage" and "every other host's
// Hangfire storage" the same thing, so a test that needs to observe real scheduling has no way to do
// it without writing into the storage every other test shares. Defaults to Hangfire's own
// "hangfire", so nothing changes for any deployment that does not set it.
var hangfireSchema = builder.Configuration.GetValue("Hangfire:SchemaName", defaultValue: "hangfire")!;

builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(
        c => c.UseNpgsqlConnection(connectionString),
        new PostgreSqlStorageOptions { SchemaName = hangfireSchema }));
builder.Services.AddHangfireServer();

// Password-reset tokens get their own short lifespan (docs/security/SECURITY-ARCHITECTURE.md §1.7:
// 30-minute window) separate from the default provider's 24h, which is intentionally kept for
// email-confirmation links (ASVS L2 review 2026-08-26, finding #8 - the default provider was
// wrongly reused for both, leaving reset links valid 48x longer than the documented design).
const string PasswordResetTokenProviderName = "PasswordReset";
builder.Services.Configure<DataProtectionTokenProviderOptions>(PasswordResetTokenProviderName, options =>
    options.TokenLifespan = TimeSpan.FromMinutes(30));

// Identity: local ASP.NET Core Identity now, MFA-ready, IdP-swappable later (00-foundational-decisions.md §2)
builder.Services
    .AddIdentityCore<AppUser>(options =>
    {
        // SECURITY-ARCHITECTURE.md §1.4/FR-IAM-003: length over composition (NIST 800-63B) - a
        // 12-char minimum with no forced symbol/case/digit rules, so passphrases aren't punished
        // in favor of predictable patterns like "Password1!". Previously 10 chars WITH forced
        // composition, backwards from the documented design.
        options.Password.RequiredLength = 12;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireDigit = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = false; // enforced manually post-login (AccountNotUsable)
        options.Tokens.PasswordResetTokenProvider = PasswordResetTokenProviderName;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders()
    .AddTokenProvider<DataProtectorTokenProvider<AppUser>>(PasswordResetTokenProviderName)
    .AddPasswordValidator<HibpBreachedPasswordValidator>();

builder.Services.AddHttpClient(nameof(HibpBreachedPasswordValidator));

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);

// SECURITY-ARCHITECTURE.md §1.1: RS256 (asymmetric), not a shared HMAC secret - workers/services
// can verify tokens holding only the public key. Built directly (not via DI) because
// AddJwtBearer's options delegate needs the validation key synchronously at registration time;
// registered as a singleton afterward so JwtTokenService (signing) shares the exact same key.
var jwtSigningKeyProvider = new JwtSigningKeyProvider(Microsoft.Extensions.Options.Options.Create(
    jwtSection.Get<JwtOptions>() ?? throw new InvalidOperationException("Jwt configuration section is missing.")));
builder.Services.AddSingleton(jwtSigningKeyProvider);

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        // Keep JWT claim names verbatim (e.g. "sub") instead of ASP.NET Core's default
        // remap to long XML-namespace claim types - HttpScopeContext reads "sub" directly.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = jwtSigningKeyProvider.GetValidationKey(),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

// NFR-SEC-004 deny-by-default (MSP-67): with no FallbackPolicy, an endpoint that simply forgets
// .RequirePermission()/.RequireAuthorization() was served anonymously. Now the default is a
// denial and public endpoints must say so out loud with .AllowAnonymous(), so the intent is
// visible in code rather than inferred from an omission. LayerDependencyTests enforces that
// every mapped endpoint declares one or the other, so this cannot silently regress.
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddHttpContextAccessor();

// Application services
builder.Services.AddScoped<IGetCurrenciesHandler, GetCurrenciesHandler>();
builder.Services.AddScoped<IGetRegionsHandler, GetRegionsHandler>();
builder.Services.AddScoped<IGetCategoriesHandler, GetCategoriesHandler>();
builder.Services.AddScoped<IGetUnitsOfMeasureHandler, GetUnitsOfMeasureHandler>();
// FEAT-06.1/FR-OFF-001: Offering CRUD.
builder.Services.AddScoped<IListOfferingsHandler, ListOfferingsHandler>();
builder.Services.AddScoped<ICreateOfferingHandler, CreateOfferingHandler>();
builder.Services.AddScoped<IUpdateOfferingHandler, UpdateOfferingHandler>();
builder.Services.AddScoped<IDeactivateOfferingHandler, DeactivateOfferingHandler>();
builder.Services.AddScoped<ISearchBuyerOfferingsHandler, SearchBuyerOfferingsHandler>();

// FEAT-11.1, pulled forward for EPIC-07.
builder.Services.AddScoped<IListEvaluationTemplatesHandler, ListEvaluationTemplatesHandler>();
builder.Services.AddScoped<IGetEvaluationTemplateHandler, GetEvaluationTemplateHandler>();
builder.Services.AddScoped<ICreateEvaluationTemplateHandler, CreateEvaluationTemplateHandler>();
builder.Services.AddScoped<IManageCriterionHandler, ManageCriterionHandler>();
builder.Services.AddScoped<IActivateEvaluationTemplateHandler, ActivateEvaluationTemplateHandler>();
builder.Services.AddScoped<IArchiveEvaluationTemplateHandler, ArchiveEvaluationTemplateHandler>();
builder.Services.AddScoped<IForkEvaluationTemplateHandler, ForkEvaluationTemplateHandler>();

// EPIC-07: RFQ authoring & lifecycle.
builder.Services.AddScoped<IListRfqsHandler, ListRfqsHandler>();
builder.Services.AddScoped<IGetRfqHandler, GetRfqHandler>();
builder.Services.AddScoped<ICreateRfqHandler, CreateRfqHandler>();
builder.Services.AddScoped<IUpdateRfqBasicsHandler, UpdateRfqBasicsHandler>();
builder.Services.AddScoped<IManageRfqItemHandler, ManageRfqItemHandler>();
builder.Services.AddScoped<IManageRequirementHandler, ManageRequirementHandler>();
builder.Services.AddScoped<IManageRfqAttachmentHandler, ManageRfqAttachmentHandler>();
builder.Services.AddScoped<IBindEvaluationTemplateHandler, BindEvaluationTemplateHandler>();
builder.Services.AddScoped<ISubmitRfqForReviewHandler, SubmitRfqForReviewHandler>();
builder.Services.AddScoped<IReturnRfqForEditsHandler, ReturnRfqForEditsHandler>();
builder.Services.AddScoped<IApproveRfqHandler, ApproveRfqHandler>();
builder.Services.AddScoped<IPublishRfqHandler, PublishRfqHandler>();
builder.Services.AddScoped<IListMyAssignmentsHandler, ListMyAssignmentsHandler>();
// EPIC-17: SCR-400, SCR-401, SCR-300.
builder.Services.AddScoped<IProcurementDashboardHandler, ProcurementDashboardHandler>();
builder.Services.AddScoped<IApprovalQueuesHandler, ApprovalQueuesHandler>();
builder.Services.AddScoped<IReviewDashboardHandler, ReviewDashboardHandler>();
builder.Services.AddScoped<ISupplierDashboardHandler, SupplierDashboardHandler>();
builder.Services.AddScoped<IRequestRfqClarificationHandler, RequestRfqClarificationHandler>();
builder.Services.AddScoped<IResolveRfqClarificationHandler, ResolveRfqClarificationHandler>();
builder.Services.AddScoped<ICloseRfqSubmissionHandler, CloseRfqSubmissionHandler>();
builder.Services.AddScoped<ICancelRfqHandler, CancelRfqHandler>();
builder.Services.AddScoped<RfqTimelineJob>();

// EPIC-08: Invitations.
builder.Services.AddScoped<IInviteSupplierHandler, InviteSupplierHandler>();
builder.Services.AddScoped<ISuggestInvitationCandidatesHandler, SuggestInvitationCandidatesHandler>();
builder.Services.AddScoped<ISupplierListInvitedRfqsHandler, SupplierListInvitedRfqsHandler>();
builder.Services.AddScoped<ISupplierGetRfqHandler, SupplierGetRfqHandler>();
builder.Services.AddScoped<ISupplierDeclineInvitationHandler, SupplierDeclineInvitationHandler>();

// EPIC-10: Clarifications.
builder.Services.AddScoped<IAnswerClarificationHandler, AnswerClarificationHandler>();
builder.Services.AddScoped<IPublishClarificationHandler, PublishClarificationHandler>();
builder.Services.AddScoped<IIssueAddendumHandler, IssueAddendumHandler>();
builder.Services.AddScoped<ISupplierPostClarificationHandler, SupplierPostClarificationHandler>();

// EPIC-09: Proposals.
builder.Services.AddScoped<IStartProposalHandler, StartProposalHandler>();
builder.Services.AddScoped<IGetProposalHandler, GetProposalHandler>();
builder.Services.AddScoped<IGetProposalByCodeHandler, GetProposalByCodeHandler>();
builder.Services.AddScoped<IManageProposalItemHandler, ManageProposalItemHandler>();
builder.Services.AddScoped<ISetCommercialTermsHandler, SetCommercialTermsHandler>();
builder.Services.AddScoped<ISetNarrativeHandler, SetNarrativeHandler>();
builder.Services.AddScoped<IAnswerRequirementHandler, AnswerRequirementHandler>();
// §12.5: the one PATCH that replaces the five per-field edit sub-routes.
builder.Services.AddScoped<IPatchProposalHandler, PatchProposalHandler>();
builder.Services.AddScoped<IManageProposalDocumentHandler, ManageProposalDocumentHandler>();
builder.Services.AddScoped<ISubmitProposalHandler, SubmitProposalHandler>();
builder.Services.AddScoped<IWithdrawProposalHandler, WithdrawProposalHandler>();
builder.Services.AddScoped<IRequestProposalClarificationHandler, RequestProposalClarificationHandler>();
builder.Services.AddScoped<IReviseProposalHandler, ReviseProposalHandler>();
builder.Services.AddSingleton<MotsSupplierPortal.Infrastructure.Security.FieldEncryptionService>();

// EPIC-11: Evaluation (two-envelope technical-qualification gate).
builder.Services.AddScoped<IOpenEvaluationHandler, OpenEvaluationHandler>();
builder.Services.AddScoped<IGetEvaluationHandler, GetEvaluationHandler>();
builder.Services.AddScoped<IAssignEvaluatorsHandler, AssignEvaluatorsHandler>();
builder.Services.AddScoped<IRecuseEvaluatorHandler, RecuseEvaluatorHandler>();
builder.Services.AddScoped<IConsolidateEvaluationHandler, ConsolidateEvaluationHandler>();
// T-028: proposal document reads - supplier's own, and the buyer's Consolidated+ gated pair.
builder.Services.AddScoped<IGetOwnProposalDocumentDownloadUrlHandler, GetOwnProposalDocumentDownloadUrlHandler>();
builder.Services.AddScoped<IGetProposalDocumentsForBuyerHandler, GetProposalDocumentsForBuyerHandler>();
builder.Services.AddScoped<IGetProposalDocumentDownloadUrlForEvaluatorHandler, GetProposalDocumentDownloadUrlForEvaluatorHandler>();
builder.Services.AddScoped<IGetProposalDocumentDownloadUrlForBuyerHandler, GetProposalDocumentDownloadUrlForBuyerHandler>();
builder.Services.AddScoped<IFinalizeEvaluationHandler, FinalizeEvaluationHandler>();
builder.Services.AddScoped<IReopenEvaluationHandler, ReopenEvaluationHandler>();
builder.Services.AddScoped<IGetMyEvaluationHandler, GetMyEvaluationHandler>();
builder.Services.AddScoped<IScoreCriterionHandler, ScoreCriterionHandler>();
builder.Services.AddScoped<ISubmitEvaluatorHandler, SubmitEvaluatorHandler>();

// EPIC-12: Comparison (derived read-side view over Proposal + Evaluation).
builder.Services.AddScoped<IGetOfferingHandler, GetOfferingHandler>();
builder.Services.AddScoped<IGetComparisonHandler, GetComparisonHandler>();

// T3-01: the RFQ attachment read path.
builder.Services.AddScoped<IGetRfqAttachmentDownloadUrlHandler, GetRfqAttachmentDownloadUrlHandler>();

// FEAT-19.4: the export engine's fonts. Singleton because a Face and a Font are native handles and
// re-creating them per request would dominate the cost of an export with thousands of rows. Both
// faces are read-only after construction and HarfBuzz shaping does not mutate them, so sharing one
// instance across requests is safe.
builder.Services.AddSingleton<MotsSupplierPortal.Infrastructure.Reporting.ReportFonts>();

// FEAT-19.1/19.2 report reads.
builder.Services.AddScoped<IProcurementReportHandler, ProcurementReportHandler>();
builder.Services.AddScoped<IComplianceReportHandler, ComplianceReportHandler>();

// EPIC-14: Award (recommendation -> approval -> issue -> ERP PO).
builder.Services.AddScoped<IGetAwardHandler, GetAwardHandler>();
builder.Services.AddScoped<IRecommendAwardHandler, RecommendAwardHandler>();
builder.Services.AddScoped<IRouteAwardForApprovalHandler, RouteAwardForApprovalHandler>();
builder.Services.AddScoped<IApproveAwardHandler, ApproveAwardHandler>();
builder.Services.AddScoped<IRejectAwardHandler, RejectAwardHandler>();
builder.Services.AddScoped<IExecuteAwardHandler, ExecuteAwardHandler>();
builder.Services.AddScoped<IRetryErpSyncHandler, RetryErpSyncHandler>();
builder.Services.AddScoped<IErpPurchaseOrderAdapter, StubErpPurchaseOrderAdapter>();
builder.Services.AddScoped<AwardErpSyncJob>();

// EPIC-13: Workspace (derived read-side guided-lifecycle view over Rfq + Invitation + Proposal + Evaluation + Award).
builder.Services.AddScoped<IGetWorkspaceHandler, GetWorkspaceHandler>();
builder.Services.AddScoped<IUpdateLegalInfoHandler, UpdateLegalInfoHandler>();
builder.Services.AddScoped<IUploadLogoHandler, UploadLogoHandler>();
builder.Services.AddScoped<IGetLogoDownloadUrlHandler, GetLogoDownloadUrlHandler>();
builder.Services.AddScoped<IManageRepresentativeHandler, ManageRepresentativeHandler>();
builder.Services.AddScoped<IGetFieldConfigHandler, GetFieldConfigHandler>();
builder.Services.AddScoped<IGetOneFieldConfigHandler, GetOneFieldConfigHandler>();
builder.Services.AddScoped<IUpdateFieldConfigHandler, UpdateFieldConfigHandler>();
builder.Services.AddScoped<IManageAddressHandler, ManageAddressHandler>();
builder.Services.AddScoped<IManageContactHandler, ManageContactHandler>();
builder.Services.AddScoped<IManageBranchHandler, ManageBranchHandler>();
builder.Services.AddScoped<IManageBankAccountHandler, ManageBankAccountHandler>();
builder.Services.AddScoped<ICreateOrganizationHandler, CreateOrganizationHandler>();
builder.Services.AddScoped<IListOrganizationsHandler, ListOrganizationsHandler>();
builder.Services.AddScoped<IManageOrgUnitHandler, ManageOrgUnitHandler>();
builder.Services.AddScoped<IManageSupplierOrgLinkHandler, ManageSupplierOrgLinkHandler>();
builder.Services.AddScoped<IManageCategoryLinkHandler, ManageCategoryLinkHandler>();
builder.Services.AddScoped<IInviteSupplierUserHandler, InviteSupplierUserHandler>();
builder.Services.AddScoped<IListSupplierUsersHandler, ListSupplierUsersHandler>();
builder.Services.AddScoped<IDisableSupplierUserHandler, DisableSupplierUserHandler>();
builder.Services.AddScoped<IAcceptSupplierUserInviteHandler, AcceptSupplierUserInviteHandler>();
builder.Services.AddScoped<ISecurityTokenService, SecurityTokenService>();
// Task #28/FR-ADM-001: staff (non-supplier) account invites - mirrors the supplier-user invite
// pair above, gated by Permissions.AdminUsersManage instead of SupplierUserManage.
builder.Services.AddScoped<IInviteStaffHandler, InviteStaffHandler>();
builder.Services.AddScoped<IAcceptStaffInviteHandler, AcceptStaffInviteHandler>();
// FR-ADM-002: role/permission admin editing.
builder.Services.AddScoped<IListRolesHandler, ListRolesHandler>();
builder.Services.AddScoped<IUpdateRolePermissionsHandler, UpdateRolePermissionsHandler>();
builder.Services.AddScoped<IRegisterSupplierHandler, RegisterSupplierHandler>();
builder.Services.AddScoped<IVerifyEmailHandler, VerifyEmailHandler>();
builder.Services.AddScoped<IResendVerificationHandler, ResendVerificationHandler>();
builder.Services.AddScoped<DraftCleanupJob>();
builder.Services.AddSingleton<MotsSupplierPortal.Api.Authorization.PerTargetRateLimiter>();
// Task #7/Stage D: the IdP seam (FR-IAM-011). AspNetIdentityProvider is the only implementation -
// swapping to a real external IdP later is a new implementation of this interface, not a change
// to LoginHandler or anything that depends on it.
builder.Services.AddScoped<IIdentityProvider, AspNetIdentityProvider>();
builder.Services.AddScoped<LoginHandler>();
builder.Services.AddScoped<ILoginHandler>(sp => sp.GetRequiredService<LoginHandler>());
builder.Services.AddScoped<IRefreshTokenHandler, RefreshTokenHandler>();
builder.Services.AddScoped<IForgotPasswordHandler, ForgotPasswordHandler>();
builder.Services.AddScoped<IResetPasswordHandler, ResetPasswordHandler>();
builder.Services.AddScoped<IEnrollMfaHandler, EnrollMfaHandler>();
builder.Services.AddScoped<IConfirmMfaEnrollmentHandler, ConfirmMfaEnrollmentHandler>();
builder.Services.AddScoped<IListSessionsHandler, ListSessionsHandler>();
builder.Services.AddScoped<IRevokeSessionHandler, RevokeSessionHandler>();
builder.Services.AddScoped<IRevokeAllSessionsHandler, RevokeAllSessionsHandler>();
// Task #35: LoggingEmailSender (still present, still unit-tested) stops being the runtime
// transport - SmtpEmailSender delivers for real, through the same durable Hangfire dispatch path
// EmailJobs already used (no second send path introduced).
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.SectionName));
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<EmailJobs>();
builder.Services.AddScoped<IGetSupplierHandler, GetSupplierHandler>();
builder.Services.AddScoped<IUpdateProfileHandler, UpdateProfileHandler>();
builder.Services.AddScoped<IAcceptTermsHandler, AcceptTermsHandler>();
builder.Services.AddScoped<ISubmitApplicationHandler, SubmitApplicationHandler>();
builder.Services.AddScoped<IListSupplierDocumentsHandler, ListSupplierDocumentsHandler>();
builder.Services.AddScoped<IListSupplierDocumentsPagedHandler, ListSupplierDocumentsPagedHandler>();
builder.Services.AddScoped<ISupplierCodeScope, SupplierCodeScope>();
builder.Services.AddScoped<IUploadDocumentHandler, UploadDocumentHandler>();
builder.Services.AddScoped<IGetDocumentDownloadUrlHandler, GetDocumentDownloadUrlHandler>();
builder.Services.AddScoped<IApproveDocumentHandler, ApproveDocumentHandler>();
builder.Services.AddScoped<IRejectDocumentHandler, RejectDocumentHandler>();
builder.Services.AddScoped<DocumentScanJob>();
builder.Services.AddScoped<DocumentExpiryJob>();
builder.Services.AddScoped<IListReviewQueueHandler, ListReviewQueueHandler>();
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
// Task #16: the missing Outbox dispatcher. IOutboxTransport is the same shape as IEmailSender -
// LoggingOutboxTransport stands in for the not-yet-built EPIC-23 ERP integration.
builder.Services.AddSingleton<IOutboxTransport, MotsSupplierPortal.Infrastructure.Suppliers.LoggingOutboxTransport>();
// EPIC-15: the dispatcher materialises notification messages rather than sending them onward.
builder.Services.AddScoped<IListNotificationsHandler, ListNotificationsHandler>();
builder.Services.AddScoped<IUnreadNotificationCountHandler, UnreadNotificationCountHandler>();
builder.Services.AddScoped<IMarkNotificationReadHandler, MarkNotificationReadHandler>();
builder.Services.AddScoped<MotsSupplierPortal.Application.Notifications.INotificationMaterialiser,
    MotsSupplierPortal.Infrastructure.Notifications.NotificationMaterialiser>();
builder.Services.AddScoped<MotsSupplierPortal.Infrastructure.Suppliers.OutboxDispatcher>();
// Singleton, constructed eagerly right after the app is built (see below) - an ObservableGauge
// that nobody ever constructs never wires up its callback, which reads as a working /metrics
// endpoint that simply never mentions the backlog. Not "reporting zero", genuinely absent.
builder.Services.AddSingleton<MotsSupplierPortal.Infrastructure.Suppliers.OutboxBacklogGauge>();
builder.Services.Configure<MinioOptions>(builder.Configuration.GetSection(MinioOptions.SectionName));
builder.Services.AddSingleton<MinioFileStorage>();
builder.Services.AddScoped<IFileStorage>(sp => sp.GetRequiredService<MinioFileStorage>());
builder.Services.Configure<ClamAvOptions>(builder.Configuration.GetSection(ClamAvOptions.SectionName));
builder.Services.AddScoped<IVirusScanner, ClamAvScanner>();
builder.Services.AddScoped<AttachmentScanner>();
builder.Services.AddScoped<MotsSupplierPortal.Application.Audit.IGetAuditLogHandler, MotsSupplierPortal.Infrastructure.Audit.GetAuditLogHandler>();
builder.Services.AddScoped<IAuditLogger, AuditLogger>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<PermissionResolver>();
builder.Services.AddScoped<IScopeContext, HttpScopeContext>();
builder.Services.AddScoped<IConcurrencyContext, HttpConcurrencyContext>();
// Scoped, not transient: every audit row written during one request must resolve the SAME
// instance, or the correlation id degrades back to being unique per call (MSP-64).
builder.Services.AddScoped<IAuditContext, MotsSupplierPortal.Api.Authorization.HttpAuditContext>();
builder.Services.AddValidatorsFromAssemblyContaining<RegisterSupplierRequestValidator>();

// Task #16/NFR-OBS-006: docs/architecture/OBSERVABILITY-ARCHITECTURE.md §5 already specified the
// exact split before any of it existed - readiness = PostgreSQL connectivity + migrations applied
// + object storage reachable + Hangfire storage reachable; liveness = process responsive, no
// dependency checks at all. ERP is explicitly NOT a readiness gate per that doc (the portal is
// ERP-independent), and ERP integration itself is not built yet (EPIC-23), so there is nothing to
// check for it here. Every readiness check is tagged "ready"; nothing is tagged for liveness, so
// liveness runs zero checks by design (see the endpoint mapping below).
builder.Services.AddSingleton(_ => JobStorage.Current);
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres", tags: ["ready"])
    .AddCheck<MotsSupplierPortal.Infrastructure.Observability.MigrationsAppliedHealthCheck>(
        "migrations", tags: ["ready"])
    .AddCheck<MotsSupplierPortal.Infrastructure.Observability.ObjectStorageHealthCheck>(
        "object-storage", tags: ["ready"])
    .AddCheck<MotsSupplierPortal.Infrastructure.Observability.HangfireStorageHealthCheck>(
        "hangfire-storage", tags: ["ready"]);

// Task #16/MSP-74: gzip + Brotli response compression.
//
// EnableForHttps defaults to false in ASP.NET Core specifically because of the CRIME/BREACH
// compression-oracle attacks, which target apps that echo attacker-controlled input back inside
// a response that ALSO carries a fixed secret (classically: a server-rendered page reflecting a
// query string next to an embedded anti-forgery token) - repeated probing with different guessed
// substrings, watching the compressed length change, leaks the secret one byte at a time. This
// API is HTTPS-only in every real environment (Secure cookies are unconditional - see
// AuthEndpoints.LoginOk), so leaving the safe default off would make this middleware a no-op in
// production, not a safer configuration. Enabled instead, because the actual shape here does not
// fit the oracle: this is a stateless JSON API returning fixed DTOs, not server-rendered pages
// reflecting free-form user input beside an embedded token, and the one real secret that
// transits (the refresh token) never appears in a compressible response body at all - it is
// HttpOnly-cookie-only, set via Set-Cookie, which response bodies compression does not touch.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        // No fallback: RequiredConfiguration guarantees this outside Development, and
        // appsettings.Development.json supplies it locally. A silent localhost default here
        // blocked the real SPA in production while looking configured.
        .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? ["http://localhost:5173"])
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()); // required so the refresh-token HttpOnly cookie is sent cross-port
});

// Per-IP anti-automation on unauthenticated auth/registration endpoints (docs/security/
// SECURITY-ARCHITECTURE.md §4; ASVS L2 review 2026-08-26, finding #3 - previously there was no
// rate limiting anywhere, leaving login/forgot-password/registration open to unthrottled
// credential-stuffing and enumeration probing beyond ASP.NET Identity's per-account lockout).
const string AuthRateLimitPolicy = "auth-strict";
// NFR-SEC-009: registration is more consequential per-request than a login/forgot-password
// attempt (writes a Supplier + AppUser row, enqueues an email), so it gets its own, tighter
// per-IP policy rather than sharing "auth-strict" - login/forgot-password/verify/resend-
// verification are unaffected. Applied only to POST /api/v1/registrations itself (route-level
// override of the group's "auth-strict"), not the whole /registrations group.
const string RegisterRateLimitPolicy = "register-strict";
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Task #16/NFR-OBS-006: task #4 made rate limiting a real defended surface - worth knowing if
    // it is actually being hit at scale, not just that it exists. Tagged by request path rather
    // than policy name: OnRejectedContext doesn't expose the matched policy's name directly, and
    // the path is at least as meaningful (distinguishes login/register/etc without guessing at
    // internal ASP.NET Core rate-limiter state). RequestServices, not a captured field, because
    // this configuration runs before the app (and its DI container) is built.
    options.OnRejected = (context, ct) =>
    {
        context.HttpContext.RequestServices
            .GetRequiredService<MotsSupplierPortal.Infrastructure.Observability.AppMetrics>()
            .RateLimitRejections.Add(1,
                new KeyValuePair<string, object?>("surface", context.HttpContext.Request.Path.Value ?? "unknown"),
                new KeyValuePair<string, object?>("layer", "per-ip"));
        return ValueTask.CompletedTask;
    };
    options.AddPolicy(AuthRateLimitPolicy, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                // Configurable so the integration host can raise it: all tests share one
                // WebApplicationFactory and therefore one per-IP partition, so the production
                // default of 10/min throttles the suite itself and surfaces as empty 429 bodies
                // that look like unrelated failures.
                PermitLimit = builder.Configuration.GetValue("RateLimiting:AuthPermitLimit", 10),
                QueueLimit = 0,
            }));
    options.AddPolicy(RegisterRateLimitPolicy, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = builder.Configuration.GetValue("RateLimiting:RegisterPermitLimit", 5),
                QueueLimit = 0,
            }));
});

var app = builder.Build();

// Task #16: forces OutboxBacklogGauge's constructor to run now, not on first (possibly never)
// resolution - a singleton nobody resolves is never built, and its ObservableGauge callback never
// gets registered with the Meter, which reads at the /metrics endpoint as "the backlog gauge
// simply is not there" rather than any kind of error.
app.Services.GetRequiredService<MotsSupplierPortal.Infrastructure.Suppliers.OutboxBacklogGauge>();

// Legal-but-questionable settings, reported once at boot. Not fatal: these configurations work,
// they just behave in a way the person who set them probably did not intend. See
// RequiredConfiguration.Warnings for why a comment on the setting is not enough.
foreach (var warning in MotsSupplierPortal.Api.Configuration.RequiredConfiguration.Warnings(app.Configuration))
{
    app.Logger.LogWarning("Configuration warning: {Warning}", warning);
}

// §7: every non-2xx (except 304) is application/problem+json. Registered BEFORE the concurrency
// handler below and before the endpoints, so it is outermost among the error-shaping middleware and
// therefore sees - and conforms - whatever they produce, including the 409 that handler writes.
app.UseMiddleware<MotsSupplierPortal.Api.Errors.ProblemDetailsMiddleware>();

app.UseSerilogRequestLogging();

// EPIC-13/FEAT-13.5/FR-PWF-005: a RowVersion (xmin) mismatch on any write throws
// DbUpdateConcurrencyException from EF Core. Before this, only Supplier profile/legal-info writes
// translated that into the documented `concurrency_conflict` 409 shape
// (Infrastructure/Suppliers/SupplierConcurrency.cs's own TryPersistAsync helper, wired into just
// two handlers) - every other aggregate's mutating endpoint (Rfq/Proposal/Evaluation/Award) let it
// propagate unhandled to a raw 500, an audit finding from this epic's own FEAT-13.5 pass. Handled
// globally, once, here, rather than wrapping every handler's own SaveChangesAsync call individually
// - and, since §8.1, answering with the documented 412 rather than the ad-hoc
// ({ error: "concurrency_conflict" }) 409 this used to emit.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (DbUpdateConcurrencyException)
    {
        // §8.1: "Stale If-Match -> 412 Precondition Failed (ETAG_MISMATCH) - the SPA refetches and
        // reconciles." This used to answer 409 { error: "concurrency_conflict" }, a shape the SPA
        // string-matched in five places. 409 now means only what §7.1 says it means - an illegal
        // state transition, a duplicate, a unique violation - and a lost update is a precondition
        // failure, which is a different thing and has its own documented status.
        //
        // The endpoint filter has already rejected a MISSING or malformed If-Match. Reaching here
        // means the caller sent a well-formed version and the database found the row had moved, so
        // this is the genuinely stale case and the only one the database can decide.
        if (!context.Response.HasStarted)
        {
            context.Response.Clear();
            await ProblemResponse.WriteAsync(context, ProblemResponse.Build(
                context, StatusCodes.Status412PreconditionFailed, ProblemTypes.PreconditionFailed,
                "The precondition failed.", "ETAG_MISMATCH",
                "This resource changed after you loaded it. Refetch it and reapply your change."));
        }
    }
});

// SECURITY-ARCHITECTURE.md §5.5: full secure-header set on every response. Applied first so it
// covers error responses too, not just successful ones.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers.Append("Strict-Transport-Security", "max-age=63072000; includeSubDomains; preload");
    headers.Append("Content-Security-Policy",
        "default-src 'self'; script-src 'self'; style-src 'self' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; img-src 'self' data: blob:; connect-src 'self'; " +
        "object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'");
    headers.Append("X-Content-Type-Options", "nosniff");
    headers.Append("X-Frame-Options", "DENY");
    headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    headers.Append("Cross-Origin-Opener-Policy", "same-origin");
    headers.Append("Cross-Origin-Resource-Policy", "same-origin");

    var path = context.Request.Path.Value ?? "";
    if (path.StartsWith("/api/v1/auth", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api/v1/registrations", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api/v1/documents", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api/v1/review", StringComparison.OrdinalIgnoreCase))
    {
        headers.Append("Cache-Control", "no-store");
    }

    await next();
});

// Must run before anything that writes a compressible response body, per the middleware's own
// ordering requirement (docs: "UseResponseCompression must be called before any middleware that
// compresses responses").
app.UseResponseCompression();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // §7: "500 responses never include stack traces, SQL, or internal messages". That is a NEGATIVE
    // about a path no ordinary request takes, so it can only be proven by deliberately taking it.
    // Development-only, and it is the subject of ErrorModelTests' planted-secret assertion.
    // S2068 fires on the literal below. The credential is fake, is never used to authenticate
    // anything, and exists precisely so a test can assert it does NOT reach the response body -
    // removing it would delete the evidence the assertion depends on. Suppressed at the site
    // rather than project-wide, so a real hard-coded credential elsewhere still fails the build.
#pragma warning disable S2068 // Hard-coded credentials are security-sensitive
    app.MapGet("/__test/throw", (Func<IResult>)(() =>
        throw new InvalidOperationException(
            "LEAK_CANARY_a7f3d2e1: connection string Host=db;Password=hunter2; at Table supplier.legal_info")))
#pragma warning restore S2068
        .AllowAnonymous()
        .WithName("TestThrow");

    using var scope = app.Services.CreateScope();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    await RoleSeeder.SeedAsync(roleManager);

    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
    await MotsSupplierPortal.Infrastructure.Identity.AdminSeeder.SeedAsync(userManager, builder.Configuration);
    if (MotsSupplierPortal.Infrastructure.Identity.AdminSeeder.TotpSecret is { } totpSecret)
    {
        // Printed once, only on the run that created the account - the account exists on every
        // later run (SeedAsync is idempotent) but the secret is only ever generated this once.
        Console.WriteLine($"[dev-seed] system_admin created: {MotsSupplierPortal.Infrastructure.Identity.AdminSeeder.Email} / {MotsSupplierPortal.Infrastructure.Identity.AdminSeeder.PasswordUsed}");
        Console.WriteLine($"[dev-seed] TOTP secret (add to an authenticator app): {totpSecret}");
    }

    await MotsSupplierPortal.Infrastructure.Identity.ReviewerSeeder.SeedAsync(userManager, builder.Configuration);
}

app.UseCors();
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Task #16/NFR-OBS-006: liveness and readiness, split per docs/architecture/
// OBSERVABILITY-ARCHITECTURE.md §5 - previously one combined "/health" answered both questions at
// once, which is exactly the failure mode a real split exists to avoid: an orchestrator restarting
// a perfectly-alive process because a dependency it doesn't own (Postgres, MinIO) is briefly down,
// or routing traffic to a replica that answered "alive" while genuinely unable to serve a request.
// Both must answer before/without authentication or they are useless to an orchestrator.
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    // Predicate that matches nothing means zero checks run - Healthy confirms only that the
    // process is up and the middleware pipeline can execute, per Microsoft's own documented
    // liveness pattern (no external dependency is ever consulted here, by design). Same JSON
    // writer as readiness below - both report an actual "checks" array, so an empty one here is
    // verifiable in the same shape as readiness's four, not merely a differently-formatted 200.
    Predicate = _ => false,
    ResponseWriter = WriteHealthResponse,
}).AllowAnonymous();

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    // NFR-OBS-006: "observable to admins" needs the per-check breakdown, not a bare "Unhealthy" -
    // an operator (or the load balancer's own logs) reading this endpoint should learn WHICH
    // dependency is down, not just that something is.
    ResponseWriter = WriteHealthResponse,
}).AllowAnonymous();

static async Task WriteHealthResponse(HttpContext context, Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport report)
{
    context.Response.ContentType = "application/json";
    var payload = new
    {
        status = report.Status.ToString(),
        checks = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString(),
            description = e.Value.Description,
        }),
        totalDurationMs = report.TotalDuration.TotalMilliseconds,
    };
    await context.Response.WriteAsJsonAsync(payload);
}

// Task #16/NFR-OBS-006: "dashboard-ready" per the ticket's own fallback - a /metrics endpoint in
// Prometheus text format, not a dashboard UI (a much bigger undertaking, and not what the
// requirement actually needs here: NFR-OBS-006 asks for background-job and Outbox health to be
// "observable to admins", and a scrape target any Prometheus/Grafana stack can point at is exactly
// that, without this app owning a rendering layer). AllowAnonymous matches this app's own /health
// precedent - a metrics scrape endpoint is conventionally reachable by an internal collector
// network, not gated by this app's own user permission system; firewalling it at the deployment/
// network level is the standard place for that control, not application code.
app.MapPrometheusScrapingEndpoint("/metrics").AllowAnonymous();

// Reference data is deliberately public: the registration form (itself unauthenticated) needs
// regions/currencies to render. These were previously anonymous only because no guard had been
// attached - now it is a stated decision (MSP-67). Contents are non-sensitive seed lists with no
// supplier or personal data.
app.MapGet("/api/v1/reference/currencies", async (IGetCurrenciesHandler handler, CancellationToken ct) =>
    {
        var currencies = await handler.HandleAsync(ct);
        return Results.Ok(currencies);
    })
    .AllowAnonymous()
    .WithName("GetCurrencies")
    .WithTags("Reference");

app.MapGet("/api/v1/reference/regions", async (IGetRegionsHandler handler, CancellationToken ct) =>
    Results.Ok(await handler.HandleAsync(ct)))
    .AllowAnonymous()
    .WithName("GetRegions")
    .WithTags("Reference");

app.MapGet("/api/v1/reference/categories", async (IGetCategoriesHandler handler, CancellationToken ct) =>
    Results.Ok(await handler.HandleAsync(ct)))
    .AllowAnonymous()
    .WithName("GetCategories")
    .WithTags("Reference");

app.MapGet("/api/v1/reference/units-of-measure", async (IGetUnitsOfMeasureHandler handler, CancellationToken ct) =>
    Results.Ok(await handler.HandleAsync(ct)))
    .AllowAnonymous()
    .WithName("GetUnitsOfMeasure")
    .WithTags("Reference");

app.MapDashboardEndpoints();
app.MapNotificationEndpoints();
app.MapRegistrationEndpoints();
app.MapAuthEndpoints();
app.MapMfaEndpoints();
app.MapSupplierEndpoints();
app.MapSupplierUserEndpoints();
app.MapAuditEndpoints();
app.MapAdminEndpoints();
app.MapDocumentEndpoints();
app.MapReviewEndpoints();
app.MapOrganizationEndpoints();
app.MapStaffEndpoints();
app.MapRoleEndpoints();
app.MapOfferingEndpoints();
app.MapEvaluationTemplateEndpoints();
app.MapRfqEndpoints();
app.MapProposalEndpoints();
app.MapEvaluationEndpoints();
app.MapComparisonEndpoints();
app.MapReportEndpoints();
app.MapAwardEndpoints();
app.MapWorkspaceEndpoints();

// MSP-87: the dashboard now requires system_admin, not merely an authenticated user. Previously
// the only gate was the deny-by-default FallbackPolicy, which closed anonymous access and nothing
// more - so any supplier_admin could read every job's arguments, including other suppliers' email
// addresses and live verification/reset tokens.
//
// FR-ADM-009 names system_admin as the actor; NFR-OBS-006 requires job health to be observable to
// admins. This is mapped in Development only, so NFR-OBS-006 is still unmet in production - a
// deliberate follow-up rather than an oversight, tracked on MSP-87. Mapping it in production is a
// separate exposure decision and is NOT taken here. The authorization shape is fixed first
// precisely because the production dashboard, when it is built, will be built on this one.
if (app.Environment.IsDevelopment())
{
    app.MapHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = [new MotsSupplierPortal.Api.Authorization.HangfireDashboardAuthorization()],
    });
}

using (var storageScope = app.Services.CreateScope())
{
    var minioStorage = storageScope.ServiceProvider.GetRequiredService<MinioFileStorage>();
    await minioStorage.EnsureBucketExistsAsync(CancellationToken.None);
}

// MSP-98: recurring jobs are SCHEDULED only when this is on, and the integration test host turns
// it off (PostgresApiFixture). Hangfire itself is untouched - the server still runs, enqueued jobs
// (emails, outbox writes) still process, and a test that asks for a job explicitly still gets it,
// because direct invocation resolves the job class from DI and never goes near the scheduler.
//
// WHY. Every job below mutates state that tests also assert on, on a cadence measured in minutes,
// against the same database the whole suite shares. That produced one loud failure - award-erp-sync
// synced an award the test had set up to fail, because the suite grew past a five-minute boundary -
// and the loud one is the lucky case. The mirror image is a test asserting a state a job ALSO
// produces (an RFQ reaching SubmissionClosed, a document reaching Expired) and passing because the
// job did the work rather than the code under test. That test goes green and stays green.
//
// A configuration switch rather than a compiled-in conditional, because the fixture already
// configures the host this way (UseSetting for the connection string, Minio, and the rest) and a
// #if or an environment check would put the test host's behaviour somewhere the test host cannot
// see it.
// The recurring job ids, in one place: the registration block below, the removal loop that makes
// suppression true of Hangfire's STORAGE rather than only of this startup, and the boot log line all
// read the same list. Three copies would drift, and the one that drifts silently is the removal
// loop - a job whose id is missing there stays scheduled under the test suite.
string[] RecurringJobIds =
[
    "document-expiry-lifecycle", "draft-registration-cleanup",
    "outbox-dispatch", "rfq-timeline", "award-erp-sync",
];

var recurringJobsEnabled = builder.Configuration.GetValue("Jobs:EnableRecurring", defaultValue: true);

// THIS HOST's job manager, resolved from DI, rather than the static RecurringJob facade. The static
// API writes to JobStorage.Current, which is process-wide: in a test process running more than one
// host, the FIRST host to initialise wins and every later host silently registers into that one's
// storage - schema override and all. Proven, not assumed: with the static API a derived host
// configured for its own schema created the schema and put all five jobs in the shared one.
// Behaviourally identical for the single-host production case.
var recurringJobs = app.Services.GetRequiredService<IRecurringJobManager>();

if (recurringJobsEnabled)
{
    recurringJobs.AddOrUpdate<DocumentExpiryJob>(
        "document-expiry-lifecycle", job => job.RunAsync(CancellationToken.None), Cron.Daily);

    recurringJobs.AddOrUpdate<MotsSupplierPortal.Infrastructure.Registrations.DraftCleanupJob>(
        "draft-registration-cleanup", job => job.RunAsync(CancellationToken.None), Cron.Daily);

    // Task #16: the dispatcher-shaped hole. Every 5 minutes, not daily like the two jobs above -
    // approval/compliance events sitting in the Outbox are meant to eventually reach an ERP, and a
    // daily cadence would make "eventually" mean "up to a day late" for no reason.
    recurringJobs.AddOrUpdate<MotsSupplierPortal.Infrastructure.Suppliers.OutboxDispatcher>(
        "outbox-dispatch", job => job.DispatchPendingAsync(CancellationToken.None), "*/5 * * * *");

    // FEAT-07.6/FR-PWF-004: RFQ submission-window open/close is time-of-day precise, not daily - same
    // 5-minute cadence reasoning as the outbox dispatcher above.
    recurringJobs.AddOrUpdate<MotsSupplierPortal.Infrastructure.Rfqs.RfqTimelineJob>(
        "rfq-timeline", job => job.RunAsync(CancellationToken.None), "*/5 * * * *");

    // EPIC-14/FEAT-14.5: same 5-minute cadence as outbox-dispatch above - the reconciliation half of
    // the Outbox -> ERP PO flow, decoupled from the award-issuing request (BRULE-077).
    recurringJobs.AddOrUpdate<AwardErpSyncJob>(
        "award-erp-sync", job => job.RunAsync(CancellationToken.None), "*/5 * * * *");
}
else
{
    // Skipping registration is not enough on its own. Hangfire PERSISTS recurring job definitions in
    // hangfire.set/hangfire.hash, so a definition written by an earlier run against the same
    // database would still be picked up and fired by this host's server. Removing them makes the
    // suppression true of the storage rather than only of this startup path.
    foreach (var jobId in RecurringJobIds)
    {
        recurringJobs.RemoveIfExists(jobId);
    }
}

// MSP-98: a misconfiguration here is otherwise INVISIBLE. Jobs:EnableRecurring defaults to true, so
// nothing changes by default - but a typo in the key (a stray case difference, a wrong section) in a
// deployed environment silently stops rfq-timeline, and RFQ submission windows then never open and
// never close. Tenders quietly stop working with no error anywhere.
//
// No test can catch that: a test asserting the correct key passes whether or not the deployed
// configuration uses the same one. This makes the state visible on boot instead of inferable later
// from the absence of behaviour. It is a mitigation, not a fix - it makes the failure loud, not
// impossible.
app.Logger.LogInformation(
    "Recurring jobs {RecurringJobsState}: {RecurringJobCount} scheduled ({RecurringJobIds})",
    recurringJobsEnabled ? "ENABLED" : "DISABLED",
    recurringJobsEnabled ? RecurringJobIds.Length : 0,
    recurringJobsEnabled ? string.Join(", ", RecurringJobIds) : "none");

app.Run();

public partial class Program; // exposed for WebApplicationFactory integration tests
