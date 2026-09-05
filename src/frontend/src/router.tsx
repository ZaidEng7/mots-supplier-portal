import { AdminOverviewPage } from './routes/admin/AdminOverviewPage'
import { SystemSettingsPage } from './routes/admin/SystemSettingsPage'
import { NotificationTemplatesPage } from './routes/admin/NotificationTemplatesPage'
import { ReferenceDataPage } from './routes/admin/ReferenceDataPage'
import { AuditExplorerPage } from './routes/admin/AuditExplorerPage'
import { MinistryOverviewPage } from './routes/ministry/MinistryOverviewPage'
import { ReportsPage } from './routes/back-office/ReportsPage'
import { lazy, Suspense } from 'react'
import { createRootRoute, createRoute, createRouter, Link, Outlet, redirect } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { LanguageSwitch } from './components/LanguageSwitch'
import { ErrorBoundaryScreen } from './components/ErrorBoundaryScreen'
import { useAuthStore } from './lib/authStore'
import { refresh } from './api/auth'

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
const NotificationsPage = lazy(() => import('./routes/NotificationsPage').then((m) => ({ default: m.NotificationsPage })))
const EvaluationDashboardPage = lazy(() => import('./routes/EvaluationDashboardPage').then((m) => ({ default: m.EvaluationDashboardPage })))
const ProcurementDashboardPage = lazy(() => import('./routes/back-office/ProcurementDashboardPage').then((m) => ({ default: m.ProcurementDashboardPage })))
const ApprovalQueuesPage = lazy(() => import('./routes/back-office/ApprovalQueuesPage').then((m) => ({ default: m.ApprovalQueuesPage })))
const ReviewDashboardPage = lazy(() => import('./routes/back-office/ReviewDashboardPage').then((m) => ({ default: m.ReviewDashboardPage })))
const BackOfficeDashboardPage = lazy(() => import('./routes/BackOfficeDashboardPage').then((m) => ({ default: m.BackOfficeDashboardPage })))
const ReviewQueuePage = lazy(() => import('./routes/ReviewQueuePage').then((m) => ({ default: m.ReviewQueuePage })))
const ReviewApplicationPage = lazy(() => import('./routes/ReviewApplicationPage').then((m) => ({ default: m.ReviewApplicationPage })))
const OrganizationsPage = lazy(() => import('./routes/back-office/OrganizationsPage').then((m) => ({ default: m.OrganizationsPage })))
const StaffPage = lazy(() => import('./routes/back-office/StaffPage').then((m) => ({ default: m.StaffPage })))
const RolesPage = lazy(() => import('./routes/back-office/RolesPage').then((m) => ({ default: m.RolesPage })))
const OfferingSearchPage = lazy(() => import('./routes/back-office/OfferingSearchPage').then((m) => ({ default: m.OfferingSearchPage })))
const EvaluationTemplatesPage = lazy(() => import('./routes/back-office/EvaluationTemplatesPage').then((m) => ({ default: m.EvaluationTemplatesPage })))
const RfqListPage = lazy(() => import('./routes/back-office/RfqListPage').then((m) => ({ default: m.RfqListPage })))
const RfqDetailPage = lazy(() => import('./routes/back-office/RfqDetailPage').then((m) => ({ default: m.RfqDetailPage })))
const MyEvaluationPage = lazy(() => import('./routes/back-office/MyEvaluationPage').then((m) => ({ default: m.MyEvaluationPage })))
const ComparisonPage = lazy(() => import('./routes/back-office/ComparisonPage').then((m) => ({ default: m.ComparisonPage })))
const AwardPage = lazy(() => import('./routes/back-office/AwardPage').then((m) => ({ default: m.AwardPage })))
const SupplierRfqListPage = lazy(() => import('./routes/SupplierRfqListPage').then((m) => ({ default: m.SupplierRfqListPage })))
const SupplierRfqDetailPage = lazy(() => import('./routes/SupplierRfqDetailPage').then((m) => ({ default: m.SupplierRfqDetailPage })))
const SupplierProposalPage = lazy(() => import('./routes/SupplierProposalPage').then((m) => ({ default: m.SupplierProposalPage })))
const SupplierShell = lazy(() => import('./shells/SupplierShell').then((m) => ({ default: m.SupplierShell })))
const BackOfficeShell = lazy(() => import('./shells/BackOfficeShell').then((m) => ({ default: m.BackOfficeShell })))

/** Ensures a valid access token is in memory before a protected route renders — on a cold load
 * (page refresh) the store is empty, so this silently exchanges the httpOnly refresh cookie for a
 * fresh one before deciding whether to redirect to /login. */
async function ensureAuthenticated(currentPath: string) {
  const state = useAuthStore.getState()
  if (state.status === 'authenticated' && state.accessToken) return

  const tokens = await refresh()
  if (tokens) {
    useAuthStore.getState().setSession(tokens.accessToken)
    return
  }

  useAuthStore.getState().clearSession()
  throw redirect({ to: '/login', search: { redirect: currentPath } })
}

const rootRoute = createRootRoute({
  component: () => (
    <Suspense fallback={null}>
      <Outlet />
    </Suspense>
  ),
  notFoundComponent: () => <ErrorBoundaryScreen code="404" />,
  errorComponent: () => <ErrorBoundaryScreen code="500" />,
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
        <span className="text-lg font-semibold" style={{ color: 'var(--color-text-brand)' }}>
          {t('appName')}
        </span>
        <div className="flex items-center gap-3">
          <LanguageSwitch />
          <Link
            to="/login"
            className="rounded-md px-3 py-1.5 text-[length:var(--text-body-sm)] font-[var(--fw-medium)]"
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
  component: () => (
    <SupplierShell>
      <Outlet />
    </SupplierShell>
  ),
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

const reviewApplicationRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/review/$referenceCode',
  component: ReviewApplicationPage,
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
  registerRoute,
  forgotPasswordRoute,
  resetPasswordRoute,
  verifyEmailRoute,
  acceptTeamInviteRoute,
  acceptStaffInviteRoute,
  evaluatorLayoutRoute.addChildren([evaluationDashboardRoute]),
  supplierLayoutRoute.addChildren([
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
  backOfficeLayoutRoute.addChildren([adminOverviewRoute, systemSettingsRoute, notificationTemplatesRoute, referenceDataRoute, auditExplorerRoute, ministryOverviewRoute, reportsRoute, procurementDashboardRoute, approvalQueuesRoute, reviewDashboardRoute, backOfficeNotificationsRoute, backOfficeDashboardRoute, reviewQueueRoute, reviewApplicationRoute, organizationsRoute, staffRoute, rolesRoute, offeringSearchRoute, evaluationTemplatesRoute, rfqListRoute, myEvaluationRoute, comparisonRoute, awardRoute, rfqDetailRoute]),
])

export const router = createRouter({ routeTree, defaultNotFoundComponent: () => <ErrorBoundaryScreen code="404" /> })

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router
  }
}
