import { AdminOverviewPage } from './routes/admin/AdminOverviewPage'
import { SystemSettingsPage } from './routes/admin/SystemSettingsPage'
import { NotificationTemplatesPage } from './routes/admin/NotificationTemplatesPage'
import { ProfilePage } from './routes/ProfilePage'
import { DocumentsPage } from './routes/DocumentsPage'
import { MyProposalsPage } from './routes/MyProposalsPage'
import { AboutPage } from './routes/AboutPage'
import { HelpPage } from './routes/HelpPage'
import { ReferenceDataPage } from './routes/admin/ReferenceDataPage'
import { OperationsPage } from './routes/admin/OperationsPage'
import { UiStringsPage } from './routes/admin/UiStringsPage'
import { EmailTemplatesPage } from './routes/admin/EmailTemplatesPage'
import { SearchPage } from './routes/SearchPage'
import { AuditExplorerPage } from './routes/admin/AuditExplorerPage'
import { MinistryOverviewPage } from './routes/ministry/MinistryOverviewPage'
// SCR-604, eager like the overview it sits beside: both are small, and ministry_viewer's whole product is
// these two screens.
import { CategoryCoveragePage } from './routes/ministry/CategoryCoveragePage'
// SCR-601/602/603/606 under D-66. Lazy, unlike the two small ministry screens above: these carry tables and
// a detail view, and a ministry_viewer opening the overview should not pay for all four.
const MinistryRfqMonitorPage = lazy(() => import('./routes/ministry/MinistryRfqMonitorPage').then((m) => ({ default: m.MinistryRfqMonitorPage })))
const MinistryRfqDetailRoute = lazy(() => import('./routes/ministry/MinistryRfqDetailPage').then((m) => ({ default: m.MinistryRfqDetailRoute })))
const MinistrySupplierRegistryPage = lazy(() => import('./routes/ministry/MinistrySupplierRegistryPage').then((m) => ({ default: m.MinistrySupplierRegistryPage })))
const MinistryAwardAnalyticsPage = lazy(() => import('./routes/ministry/MinistryAwardAnalyticsPage').then((m) => ({ default: m.MinistryAwardAnalyticsPage })))
import { ReportsPage } from './routes/back-office/ReportsPage'
import { lazy, Suspense } from 'react'
import { createRootRoute, createRoute, createRouter, Link, Outlet, redirect } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { LanguageSwitch } from './components/LanguageSwitch'
import { ErrorBoundaryScreen } from './components/ErrorBoundaryScreen'
import { useAuthStore } from './lib/authStore'
import i18n from 'i18next'
import { SessionExpiredOverlay } from './components/SessionExpiredOverlay'
import { MaintenanceBanner } from './components/MaintenanceBanner'
import { PublicFooter } from './components/PublicFooter'
import { FirstRunLocale } from './components/FirstRunLocale'
import { refresh, getAccount } from './api/auth'

// Route-level code splitting (docs/architecture/00-foundational-decisions.md: "Web perf LCP <
// 2.5s ... route-level code splitting"). A single unsplit bundle measured ~3.4s LCP under
// Lighthouse's mobile/4G throttling - well over budget - because every route pulled in every
// other route's code (Radix, react-hook-form, zod) on first paint. Lazy-loading each route
// component means the initial chunk only needs the router shell.
const HomePage = lazy(() => import('./routes/HomePage').then((m) => ({ default: m.HomePage })))
const LoginPage = lazy(() => import('./routes/LoginPage').then((m) => ({ default: m.LoginPage })))
const RegisterPage = lazy(() => import('./routes/RegisterPage').then((m) => ({ default: m.RegisterPage })))
const ForgotPasswordPage = lazy(() => import('./routes/ForgotPasswordPage').then((m) => ({ default: m.ForgotPasswordPage })))
const ResetPasswordPage = lazy(() => import('./routes/ResetPasswordPage').then((m) => ({ default: m.ResetPasswordPage })))
const VerifyEmailPage = lazy(() => import('./routes/VerifyEmailPage').then((m) => ({ default: m.VerifyEmailPage })))
const AcceptTeamInvitePage = lazy(() => import('./routes/AcceptTeamInvitePage').then((m) => ({ default: m.AcceptTeamInvitePage })))
const AcceptStaffInvitePage = lazy(() => import('./routes/AcceptStaffInvitePage').then((m) => ({ default: m.AcceptStaffInvitePage })))
const SupplierDashboardPage = lazy(() => import('./routes/SupplierDashboardPage').then((m) => ({ default: m.SupplierDashboardPage })))
const OnboardingPage = lazy(() => import('./routes/OnboardingPage').then((m) => ({ default: m.OnboardingPage })))
const ContactsPage = lazy(() => import('./routes/onboarding/ContactsPage').then((m) => ({ default: m.ContactsPage })))
const AddressesPage = lazy(() => import('./routes/onboarding/AddressesPage').then((m) => ({ default: m.AddressesPage })))
const BankingPage = lazy(() => import('./routes/onboarding/BankingPage').then((m) => ({ default: m.BankingPage })))
const OfferingsPage = lazy(() => import('./routes/onboarding/OfferingsPage').then((m) => ({ default: m.OfferingsPage })))
const TeamPage = lazy(() => import('./routes/TeamPage').then((m) => ({ default: m.TeamPage })))
const OfferingCatalogPage = lazy(() => import('./routes/OfferingCatalogPage').then((m) => ({ default: m.OfferingCatalogPage })))
const SettingsPage = lazy(() => import('./routes/SettingsPage').then((m) => ({ default: m.SettingsPage })))
// SCR-901.
const NotificationPreferencesPage = lazy(() => import('./routes/NotificationPreferencesPage').then((m) => ({ default: m.NotificationPreferencesPage })))
const NotificationsPage = lazy(() => import('./routes/NotificationsPage').then((m) => ({ default: m.NotificationsPage })))
const EvaluationDashboardPage = lazy(() => import('./routes/EvaluationDashboardPage').then((m) => ({ default: m.EvaluationDashboardPage })))
const ProcurementDashboardPage = lazy(() => import('./routes/back-office/ProcurementDashboardPage').then((m) => ({ default: m.ProcurementDashboardPage })))
const ApprovalQueuesPage = lazy(() => import('./routes/back-office/ApprovalQueuesPage').then((m) => ({ default: m.ApprovalQueuesPage })))
const ReviewDashboardPage = lazy(() => import('./routes/back-office/ReviewDashboardPage').then((m) => ({ default: m.ReviewDashboardPage })))
const BackOfficeDashboardPage = lazy(() => import('./routes/BackOfficeDashboardPage').then((m) => ({ default: m.BackOfficeDashboardPage })))
const ReviewQueuePage = lazy(() => import('./routes/ReviewQueuePage').then((m) => ({ default: m.ReviewQueuePage })))
const ReviewApplicationPage = lazy(() => import('./routes/ReviewApplicationPage').then((m) => ({ default: m.ReviewApplicationPage })))
// SCR-307 and SCR-402, the two directories. Lazy like every other back-office screen: neither is on the
// path a reviewer or an officer takes on sign-in, so neither belongs in the first bundle.
const ComplianceDirectoryPage = lazy(() => import('./routes/ComplianceDirectoryPage').then((m) => ({ default: m.ComplianceDirectoryPage })))
const SupplierDirectoryPage = lazy(() => import('./routes/back-office/SupplierDirectoryPage').then((m) => ({ default: m.SupplierDirectoryPage })))
const OrganizationsPage = lazy(() => import('./routes/back-office/OrganizationsPage').then((m) => ({ default: m.OrganizationsPage })))
const StaffPage = lazy(() => import('./routes/back-office/StaffPage').then((m) => ({ default: m.StaffPage })))
const RolesPage = lazy(() => import('./routes/back-office/RolesPage').then((m) => ({ default: m.RolesPage })))
const OfferingSearchPage = lazy(() => import('./routes/back-office/OfferingSearchPage').then((m) => ({ default: m.OfferingSearchPage })))
const EvaluationTemplatesPage = lazy(() => import('./routes/back-office/EvaluationTemplatesPage').then((m) => ({ default: m.EvaluationTemplatesPage })))
const RfqListPage = lazy(() => import('./routes/back-office/RfqListPage').then((m) => ({ default: m.RfqListPage })))
const RfqDetailPage = lazy(() => import('./routes/back-office/RfqDetailPage').then((m) => ({ default: m.RfqDetailPage })))
const MyEvaluationPage = lazy(() => import('./routes/back-office/MyEvaluationPage').then((m) => ({ default: m.MyEvaluationPage })))
// SCR-501.
const MyEvaluationBriefRoute = lazy(() => import('./routes/back-office/MyEvaluationBriefPage').then((m) => ({ default: m.MyEvaluationBriefRoute })))
const ComparisonPage = lazy(() => import('./routes/back-office/ComparisonPage').then((m) => ({ default: m.ComparisonPage })))
const ReceivedProposalsPage = lazy(() => import('./routes/back-office/ReceivedProposalsPage').then((m) => ({ default: m.ReceivedProposalsPage })))
const AwardPage = lazy(() => import('./routes/back-office/AwardPage').then((m) => ({ default: m.AwardPage })))
const SupplierRfqListPage = lazy(() => import('./routes/SupplierRfqListPage').then((m) => ({ default: m.SupplierRfqListPage })))
const SupplierRfqDetailPage = lazy(() => import('./routes/SupplierRfqDetailPage').then((m) => ({ default: m.SupplierRfqDetailPage })))
const SupplierProposalPage = lazy(() => import('./routes/SupplierProposalPage').then((m) => ({ default: m.SupplierProposalPage })))
const SupplierShell = lazy(() => import('./shells/SupplierShell').then((m) => ({ default: m.SupplierShell })))
const BackOfficeShell = lazy(() => import('./shells/BackOfficeShell').then((m) => ({ default: m.BackOfficeShell })))

/** Ensures a valid access token is in memory before a protected route renders — on a cold load
 * (page refresh) the store is empty, so this silently exchanges the httpOnly refresh cookie for a
 * fresh one before deciding whether to redirect to /login. */
/**
 * SCR-902's other half: a stored interface language nobody read would be a setting that does nothing.
 *
 * <p>Runs once per app load, after a session exists, and never again - a mid-session toggle has to
 * keep winning over the value this fetched, or switching language would undo itself on the next
 * navigation. Fire-and-forget, because no screen should wait on a preference.</p>
 */
let languageApplied = false
function applyStoredLanguage() {
  if (languageApplied) return
  languageApplied = true
  void getAccount()
    .then((account) => (i18n.language === account.language ? undefined : i18n.changeLanguage(account.language)))
    .catch(() => undefined)
}

async function ensureAuthenticated(currentPath: string) {
  const state = useAuthStore.getState()
  if (state.status === 'authenticated' && state.accessToken) {
    applyStoredLanguage()
    return
  }

  const tokens = await refresh()
  if (tokens) {
    useAuthStore.getState().setSession(tokens.accessToken)
    applyStoredLanguage()
    return
  }

  useAuthStore.getState().clearSession()
  throw redirect({ to: '/login', search: { redirect: currentPath } })
}

const rootRoute = createRootRoute({
  component: () => (
    <Suspense fallback={null}>
      {/* SCR-044. Above the Outlet so it is the first thing on every page, authenticated or not. */}
      <MaintenanceBanner />
      <Outlet />
      {/* SCR-040. Mounted at the root so an expiry is covered on every page, and OUTSIDE the Outlet so
          re-authenticating does not remount the route underneath and discard the work it is protecting. */}
      {/* SCR-908/SCR-907 reachable at last - see PublicFooter. Below the Outlet so it sits under the
          page content, and inside the root so an anonymous user on /login has it too. */}
      <PublicFooter />
      <SessionExpiredOverlay />
      {/* SCR-010. Below the expiry overlay in stacking order (--z-modal against --z-tooltip): if a session lapses
          while the language question is open, the expiry is the one that has to be answered first. */}
      <FirstRunLocale />
    </Suspense>
  ),
  notFoundComponent: () => <ErrorBoundaryScreen code="404" />,
  errorComponent: () => <ErrorBoundaryScreen code="500" />,
})

// SCR-908, `/about`, public. Outside every authenticated layout on purpose: the moment a user most
// needs to say which build they are on is when they cannot sign in.
const aboutRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/about',
  component: AboutPage,
})

const loginRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/login',
  validateSearch: (search: Record<string, unknown>): { redirect?: string } => ({
    redirect: typeof search.redirect === 'string' ? search.redirect : undefined,
  }),
  component: LoginPage,
})

const registerRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/register',
  component: RegisterPage,
})

const forgotPasswordRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/forgot-password',
  component: ForgotPasswordPage,
})

const resetPasswordRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/reset-password',
  validateSearch: (search: Record<string, unknown>): { token?: string } => ({
    token: typeof search.token === 'string' ? search.token : undefined,
  }),
  component: ResetPasswordPage,
})

const verifyEmailRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/verify-email',
  validateSearch: (search: Record<string, unknown>): { token?: string } => ({
    token: typeof search.token === 'string' ? search.token : undefined,
  }),
  component: VerifyEmailPage,
})

const acceptTeamInviteRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/accept-invite',
  validateSearch: (search: Record<string, unknown>): { token?: string } => ({
    token: typeof search.token === 'string' ? search.token : undefined,
  }),
  component: AcceptTeamInvitePage,
})

const acceptStaffInviteRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/accept-staff-invite',
  validateSearch: (search: Record<string, unknown>): { token?: string } => ({
    token: typeof search.token === 'string' ? search.token : undefined,
  }),
  component: AcceptStaffInvitePage,
})

function IndexPage() {
  const { t } = useTranslation()
  return (
    <div className="flex min-h-screen flex-col" style={{ backgroundColor: 'var(--color-bg-app)' }}>
      <header
        className="flex items-center justify-between border-b px-6 py-4"
        style={{ borderColor: 'var(--color-border)', backgroundColor: 'var(--color-bg-surface)' }}
      >
        <span className="text-[length:var(--text-h4)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-brand)' }}>
          {t('appName')}
        </span>
        <div className="flex items-center gap-3">
          <LanguageSwitch />
          <Link
            to="/login"
            className="rounded-[var(--radius-md)] px-3 py-1.5 text-[length:var(--text-body-sm)] font-[var(--fw-medium)]"
            style={{ backgroundColor: 'var(--color-brand-solid)', color: 'var(--color-text-inverse)' }}
          >
            {t('auth.submit')}
          </Link>
        </div>
      </header>
      <main className="flex flex-1 flex-col px-6 py-8">
        <HomePage />
      </main>
    </div>
  )
}

const indexRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/',
  component: IndexPage,
})

// --- Supplier app shell (protected) ---
const supplierLayoutRoute = createRoute({
  getParentRoute: () => rootRoute,
  id: 'supplier-layout',
  beforeLoad: async () => ensureAuthenticated('/dashboard'),
  // The mirror of the back-office guard below, which has refused suppliers since it was written.
  //
  // This side had only ensureAuthenticated - authenticated, not "is a supplier" - so any signed-in
  // account could land in the supplier shell, and every screen in it then asked /suppliers/me/... and
  // got a 404. Seen for real: a system_admin signing in with a stale `?redirect=/dashboard` from a
  // previous session landed on a supplier dashboard that could never load.
  component: () => {
    const claims = useAuthStore.getState().claims
    if (!claims?.supplierId) {
      return <ErrorBoundaryScreen code="403" />
    }
    return (
      <SupplierShell>
        <Outlet />
      </SupplierShell>
    )
  },
})

const supplierDashboardRoute = createRoute({
  getParentRoute: () => supplierLayoutRoute,
  path: '/dashboard',
  component: SupplierDashboardPage,
})

const onboardingRoute = createRoute({
  getParentRoute: () => supplierLayoutRoute,
  path: '/onboarding',
  component: OnboardingPage,
})

const onboardingContactsRoute = createRoute({
  getParentRoute: () => supplierLayoutRoute,
  path: '/onboarding/contacts',
  component: ContactsPage,
})

const onboardingAddressesRoute = createRoute({
  getParentRoute: () => supplierLayoutRoute,
  path: '/onboarding/addresses',
  component: AddressesPage,
})

const onboardingBankingRoute = createRoute({
  getParentRoute: () => supplierLayoutRoute,
  path: '/onboarding/banking',
  component: BankingPage,
})

const onboardingOfferingsRoute = createRoute({
  getParentRoute: () => supplierLayoutRoute,
  path: '/onboarding/offerings',
  component: OfferingsPage,
})

// SCR-121, `/profile`, supplier_admin + supplier_user, P0. The supplier's own read of their own
// profile - which existed as an endpoint since EPIC-01 and was rendered only by the REVIEWER's
// screen. SCR-122..126's entry points live on it, linking to the editors that already exist rather
// than growing second copies of them.
const profileRoute = createRoute({
  getParentRoute: () => supplierLayoutRoute,
  path: '/profile',
  component: ProfilePage,
})

// SCR-130 (P0) + SCR-131 + SCR-132 + SCR-133, `/documents`, supplier_admin + supplier_user.
// SCR-133 is a filter on this page rather than its own route: "needs attention" is a view of the
// same list, and a second screen would be a second place for "expiring" to be defined.
const documentsRoute = createRoute({
  getParentRoute: () => supplierLayoutRoute,
  path: '/documents',
  component: DocumentsPage,
})

// SCR-150, `/proposals`, supplier_admin + supplier_user, P0. The missing index: SCR-154's read,
// SCR-155's revise, SCR-156's withdraw and SCR-157's award response all already live in the proposal
// workspace on the RFQ, and each row links back into it rather than duplicating any of them.
const myProposalsRoute = createRoute({
  getParentRoute: () => supplierLayoutRoute,
  path: '/proposals',
  component: MyProposalsPage,
})

const teamRoute = createRoute({
  getParentRoute: () => supplierLayoutRoute,
  path: '/team',
  component: TeamPage,
})

const offeringCatalogRoute = createRoute({
  getParentRoute: () => supplierLayoutRoute,
  path: '/offerings',
  component: OfferingCatalogPage,
})

// SCR-500 sits at "/evaluation" (SCREEN-INVENTORY's own route column, and what the epic names)
// while IA §4.3 puts the evaluator's dashboard at "/bo". The two documents disagree; the inventory's
// explicit route wins, and the conflict is reported. It renders in the back-office chrome because
// §4.3 is unambiguous that an evaluator "enters the Back-office shell" - so this is a pathless
// layout route, the same shape supplierLayoutRoute already uses, rather than a "/back-office" child
// that would change the URL.
const evaluatorLayoutRoute = createRoute({
  getParentRoute: () => rootRoute,
  id: 'evaluator-layout',
  beforeLoad: async () => ensureAuthenticated('/evaluation'),
  component: () => (
    <BackOfficeShell>
      <Outlet />
    </BackOfficeShell>
  ),
})

const evaluationDashboardRoute = createRoute({
  getParentRoute: () => evaluatorLayoutRoute,
  path: '/evaluation',
  component: EvaluationDashboardPage,
})

// SCR-900: "/notifications", all authenticated personas. Registered under BOTH shells rather than
// once, because the two shells are two different URL spaces - a back-office user has no route under
// the supplier layout at all. SCREEN-INVENTORY names one path; this is the same SCREEN reached
// through each persona's own shell, which is the closest the router can come to that without giving
// staff a supplier chrome.
// EPIC-17. SCREEN-INVENTORY routes these at /procurement, /procurement/approvals and /review; they
// render in the back-office chrome their personas already live in, so they hang off that layout and
// their real paths carry the /back-office prefix. The inventory's paths and this app's URL space
// disagree here exactly as they did for SCR-500 - reported rather than resolved by renaming a shell.
const procurementDashboardRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/procurement',
  component: ProcurementDashboardPage,
})

// FEAT-19.1/19.2. The IA routes reports at "/bo/reports"; like SCR-400 and SCR-500 before it, that
// path and this app's URL space disagree, so it hangs off the back-office layout and its real path
// carries the /back-office prefix. Reported rather than resolved by renaming a shell.
//
// No SCR id: the screen has no specification at all, and an invented id would corrupt an inventory
// that the specifications, backlog and tests all cross-reference.
const reportsRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/reports',
  component: ReportsPage,
})

// SCR-600, `/ministry`, ministry_viewer, P1. The specification's own path, and it matches this app's
// URL space - unlike SCR-400/500/reports, no prefix disagreement to report here.
//
// Under the BACK-OFFICE layout: ministry_viewer is staff, not a supplier, and that layout is what
// already refuses a supplier-scoped session with a 403.
const ministryOverviewRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/ministry',
  component: MinistryOverviewPage,
})

// SCR-604, under the same layout as the overview it belongs beside.
const categoryCoverageRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/ministry/categories',
  component: CategoryCoveragePage,
})

// SCR-602. Declared before the detail route it parents, for the same reason the compliance directory is:
// a literal segment must not be read as a reference code.
const ministryRfqMonitorRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/ministry/rfqs',
  component: MinistryRfqMonitorPage,
})

// SCR-606.
const ministryRfqDetailRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/ministry/rfqs/$referenceCode',
  component: MinistryRfqDetailRoute,
})

// SCR-601.
const ministrySupplierRegistryRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/ministry/suppliers',
  component: MinistrySupplierRegistryPage,
})

// SCR-603.
const ministryAwardAnalyticsRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/ministry/awards',
  component: MinistryAwardAnalyticsPage,
})

// SCR-700, `/back-office/admin`, system_admin, P1 (FR-DSH-006). The specification writes SCR-700's
// path as `/admin`; this app keeps every staff screen under `/back-office`, so the prefix disagreement
// is the same one already reported for SCR-400/500 and reports - noted, not silently resolved.
const adminOverviewRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/admin',
  component: AdminOverviewPage,
})

// SCR-724, `/back-office/settings`, system_admin, P1 (FR-ADM-006). Same `/admin` -> `/back-office`
// prefix note as SCR-700 above.
const systemSettingsRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/settings',
  component: SystemSettingsPage,
})

// SCR-715, `/back-office/notification-templates`, system_admin, P1 (FR-ADM-007). SCREEN-INVENTORY
// writes it as `/admin/notifications/templates`; flattened here because `/back-office/notifications`
// is already this app's notification INBOX, and nesting an admin editor under a persona's own inbox
// route would make the two read as the same feature. Reported, not silently resolved.
const notificationTemplatesRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/notification-templates',
  component: NotificationTemplatesPage,
})

// SCR-710/711/712, `/back-office/reference`, `system_admin`, P1 (FR-ADM-004). SCREEN-INVENTORY gives
// the three tables three paths under `/admin`; one route serves all five because the operations are
// identical and only DocumentType carries extra flags - five near-identical screens would be five
// places for the next change to miss, which is the argument the single endpoint family already makes.
// The `/admin` -> `/back-office` prefix note from SCR-700 applies here too.
const referenceDataRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/reference',
  component: ReferenceDataPage,
})

// SCR-720, `/back-office/audit`, `system_admin`, P2 (FR-AUD-004). SCREEN-INVENTORY writes the path as
// `/admin/audit`; same `/admin` -> `/back-office` prefix note as SCR-700.
const auditExplorerRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/audit',
  component: AuditExplorerPage,
})

// SCR-430 + SCR-431, `/back-office/rfqs/$referenceCode/proposals`, procurement_officer and
// procurement_manager, both P0 (T-082). Two inventory rows on one route: the detail is a panel, not
// a navigation, for the same reason the comparison matrix is a matrix.
const receivedProposalsRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/rfqs/$referenceCode/proposals',
  component: ReceivedProposalsPage,
})

const approvalQueuesRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/procurement/approvals',
  component: ApprovalQueuesPage,
})

const reviewDashboardRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/review-dashboard',
  component: ReviewDashboardPage,
})

const notificationsRoute = createRoute({
  getParentRoute: () => supplierLayoutRoute,
  path: '/notifications',
  component: NotificationsPage,
})

const backOfficeNotificationsRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/notifications',
  component: NotificationsPage,
})

const settingsRoute = createRoute({
  getParentRoute: () => supplierLayoutRoute,
  path: '/settings',
  component: SettingsPage,
})

// SCR-901, under BOTH shells. The inventory lists it for "all authenticated", and a supplier reads it for the
// same reason staff do - they receive invitations and award offers, and this is the screen that says which of
// those they cannot switch off.
const notificationPreferencesRoute = createRoute({
  getParentRoute: () => supplierLayoutRoute,
  path: '/settings/notifications',
  component: NotificationPreferencesPage,
})

const backOfficeNotificationPreferencesRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/account/notifications',
  component: NotificationPreferencesPage,
})

// SCR-902 is "all authenticated", and until now the settings screen existed only under the supplier
// shell: a procurement officer, evaluator, reviewer or admin had no way to change their own password,
// enrol MFA, see their sessions or fix their own name. The SAME page is mounted here rather than a
// second one written for staff - every card on it is about the caller's own account, and the one
// supplier-scoped card gates itself on being a supplier.
// SCR-907, `/help` and `/back-office/help`. One page, mounted in both shells so it keeps the nav the
// reader came from - a help link that drops a procurement officer into the supplier chrome is worse
// than no link.
const helpRoute = createRoute({
  getParentRoute: () => supplierLayoutRoute,
  path: '/help',
  component: HelpPage,
})

const backOfficeHelpRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/help',
  component: HelpPage,
})

// SCR-721 + SCR-722, `/back-office/operations`, system_admin.
const operationsRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/operations',
  component: OperationsPage,
})

// SCR-716, `/back-office/ui-strings`, system_admin.
const uiStringsRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/ui-strings',
  component: UiStringsPage,
})

// SCR-906, `/back-office/search`, back-office personas. Not gated in the router: what a caller may find
// is decided per entity kind on the server, and a route-level permission would either lock out someone who
// can legitimately search one kind or admit someone who can search none.
const searchRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/search',
  component: SearchPage,
})

// T-076, `/back-office/email-templates`, system_admin.
const emailTemplatesRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/email-templates',
  component: EmailTemplatesPage,
})

const backOfficeAccountRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/account',
  component: SettingsPage,
})

const supplierRfqListRoute = createRoute({
  getParentRoute: () => supplierLayoutRoute,
  path: '/rfqs',
  component: SupplierRfqListPage,
})

const supplierRfqDetailRoute = createRoute({
  getParentRoute: () => supplierLayoutRoute,
  path: '/rfqs/$referenceCode',
  component: SupplierRfqDetailPage,
})

const supplierProposalRoute = createRoute({
  getParentRoute: () => supplierLayoutRoute,
  path: '/rfqs/$referenceCode/proposal',
  component: SupplierProposalPage,
})

// --- Back-office app shell (protected, staff-only: no supplierId claim) ---
const backOfficeLayoutRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/back-office',
  beforeLoad: async () => ensureAuthenticated('/back-office/dashboard'),
  component: () => {
    const claims = useAuthStore.getState().claims
    if (claims?.supplierId) {
      return <ErrorBoundaryScreen code="403" />
    }
    return (
      <BackOfficeShell>
        <Outlet />
      </BackOfficeShell>
    )
  },
})

const backOfficeDashboardRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/dashboard',
  component: BackOfficeDashboardPage,
})

const reviewQueueRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/review',
  component: ReviewQueuePage,
})

// SCR-307. A static segment under the same parent as '/review/$referenceCode': TanStack matches the
// literal before the parameter, so 'suppliers' is not read as a supplier reference code.
const complianceDirectoryRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/review/suppliers',
  component: ComplianceDirectoryPage,
})

const reviewApplicationRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/review/$referenceCode',
  component: ReviewApplicationPage,
})

// SCR-402.
const supplierDirectoryRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/suppliers',
  component: SupplierDirectoryPage,
})

const organizationsRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/organizations',
  component: OrganizationsPage,
})

const staffRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/staff',
  component: StaffPage,
})

const rolesRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/roles',
  component: RolesPage,
})

const offeringSearchRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/offerings',
  component: OfferingSearchPage,
})

const evaluationTemplatesRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/evaluation-templates',
  component: EvaluationTemplatesPage,
})

const rfqListRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/rfqs',
  component: RfqListPage,
})

const rfqDetailRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/rfqs/$referenceCode',
  component: RfqDetailPage,
})

const myEvaluationRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/rfqs/$referenceCode/my-evaluation',
  component: MyEvaluationPage,
})

// SCR-501. Beside the scoring screen rather than inside it: an evaluator reads the brief before they
// start and returns to it when a criterion is ambiguous, and folding it into the form would put a wall of
// instruction in front of the score fields every time.
const myEvaluationBriefRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/rfqs/$referenceCode/brief',
  component: MyEvaluationBriefRoute,
})

const comparisonRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/rfqs/$referenceCode/comparison',
  component: ComparisonPage,
})

const awardRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/rfqs/$referenceCode/award',
  component: AwardPage,
})

const routeTree = rootRoute.addChildren([
  indexRoute,
  loginRoute,
  aboutRoute,
  registerRoute,
  forgotPasswordRoute,
  resetPasswordRoute,
  verifyEmailRoute,
  acceptTeamInviteRoute,
  acceptStaffInviteRoute,
  evaluatorLayoutRoute.addChildren([evaluationDashboardRoute]),
  supplierLayoutRoute.addChildren([profileRoute, notificationPreferencesRoute, documentsRoute, myProposalsRoute, helpRoute, 
    supplierDashboardRoute,
    onboardingRoute,
    onboardingContactsRoute,
    onboardingAddressesRoute,
    onboardingBankingRoute,
    onboardingOfferingsRoute,
    teamRoute,
    offeringCatalogRoute,
    settingsRoute,
    notificationsRoute,
    supplierRfqListRoute,
    supplierRfqDetailRoute,
    supplierProposalRoute,
  ]),
  backOfficeLayoutRoute.addChildren([adminOverviewRoute, systemSettingsRoute, notificationTemplatesRoute, referenceDataRoute, auditExplorerRoute, ministryOverviewRoute, categoryCoverageRoute, ministryRfqMonitorRoute, ministryRfqDetailRoute, ministrySupplierRegistryRoute, ministryAwardAnalyticsRoute, reportsRoute, procurementDashboardRoute, approvalQueuesRoute, reviewDashboardRoute, backOfficeNotificationsRoute, backOfficeAccountRoute, backOfficeNotificationPreferencesRoute, backOfficeHelpRoute, operationsRoute, uiStringsRoute, searchRoute, emailTemplatesRoute, backOfficeDashboardRoute, reviewQueueRoute, complianceDirectoryRoute, reviewApplicationRoute, supplierDirectoryRoute, organizationsRoute, staffRoute, rolesRoute, offeringSearchRoute, evaluationTemplatesRoute, rfqListRoute, myEvaluationRoute, myEvaluationBriefRoute, comparisonRoute, awardRoute, receivedProposalsRoute, rfqDetailRoute]),
])

export const router = createRouter({ routeTree, defaultNotFoundComponent: () => <ErrorBoundaryScreen code="404" /> })

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router
  }
}
