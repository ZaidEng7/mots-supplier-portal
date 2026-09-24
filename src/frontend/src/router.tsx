// Every route this product has, the two shells they hang off, and the guards on each.
//
// ROUTE-LEVEL CODE SPLITTING, per 00-foundational-decisions.md's "Web perf LCP < 2.5s ... route-level code splitting". A single
// unsplit bundle measured about 3.4s LCP under Lighthouse's mobile and 4G throttling - well over budget - because every route
// pulled in every other route's code, Radix and react-hook-form and zod included, on first paint. Lazy-loading each route
// component means the initial chunk only needs the router shell. Two screens are EAGER: SCR-600 and SCR-604, both small, and
// ministry_viewer's whole product is those two. SCR-601, 602, 603 and 606 are lazy despite belonging to the same persona,
// because they carry tables and a detail view and a ministry_viewer opening the overview should not pay for all four.
//
// HOW THE SPLITTING IS DONE matters as much as that it is done. Screens were React.lazy, and React holds a Suspense boundary's
// content back for up to 300ms after it last showed a fallback, so a first visit whose code arrived in 50ms still sat blank for
// 300: 404ms from click to heading on the supplier directory, with its data taking 25. Screens now use lazyRouteComponent, which
// renders synchronously once loaded and exposes preload(). defaultPreload 'intent' starts that load on hover, focus or touch, and
// the router awaits it during navigation, so a screen never enters a fallback on a click: 113-124ms on a plain click, 73-83ms
// with a hover first. It loads only what someone is about to open, not everything - preloading all screens up front would add
// 272KB gzipped to a 219KB entry, which is the 4G budget above spent in reverse. HomePage and the two shells stay React.lazy:
// they render inside layouts rather than as route components, so the router cannot preload them, and each loads once a session.
//
// THE TWO GUARDS. ensureAuthenticated makes sure a valid access token is in memory before a protected route renders: on a cold
// load the store is empty, so it silently exchanges the httpOnly refresh cookie for a fresh one before deciding whether to
// redirect to /login. And SCR-902's other half reads the stored interface language, because a stored language nobody read would
// be a setting that does nothing - once per app load, after a session exists, and never again, so that a mid-session toggle
// keeps winning over the value it fetched rather than switching language undoing itself on the next navigation. It is
// fire-and-forget, because no screen should wait on a preference.
//
// BOTH SHELLS REFUSE THE OTHER PERSONA, and until recently only one of them did. The supplier layout had ensureAuthenticated
// alone - authenticated, not "is a supplier" - so any signed-in account could land in the supplier shell and every screen in it
// then asked /suppliers/me/... and got a 404. Seen for real: a system_admin signing in with a stale ?redirect=/dashboard from a
// previous session landed on a supplier dashboard that could never load. The evaluator's pathless layout had the same hole
// arriving by the one door nobody had closed: a supplier session that reached /evaluation got the back-office chrome, the dark
// staff rail included, and then a screen whose every query answered 404. There a supplier is REFUSED rather than redirected,
// which is what the other two do - a redirect would bounce somebody who followed a link into a loop, and a 403 says what
// happened. That 403 stands where the shell would, so it carries its own link home, to the dashboard of the session's persona.
//
// A SCREEN THAT THROWS IS CAUGHT INSIDE ITS SHELL. Only the root declared an errorComponent, and TanStack Router wraps a match in
// a catch boundary only when that match has an error component of its own or the router has a default. So a screen that threw
// while rendering - a lazy chunk that still failed after lazyRouteComponent's one automatic reload included - was caught at the
// root, and its 500 replaced the sidebar, the top bar and the breadcrumb along with the screen. defaultErrorComponent gives every
// match its own boundary, so a screen's error now renders inside its layout's PageOutlet with the shell still standing. The root
// keeps its own errorComponent for anything that fails above the layouts.
//
// FIVE PATH DISAGREEMENTS WITH THE INVENTORY are reported rather than resolved by renaming a shell. SCR-500 sits at /evaluation,
// which is SCREEN-INVENTORY's own route column and what the epic names, while IA §4.3 puts the evaluator's dashboard at /bo;
// the inventory's explicit route wins. It renders in the back-office chrome because §4.3 is unambiguous that an evaluator
// "enters the Back-office shell", so it is a pathless layout route - the same shape supplierLayoutRoute uses - rather than a
// /back-office child that would change the URL. EPIC-17's three screens are routed at /procurement, /procurement/approvals and
// /review by the inventory and render in the back-office chrome their personas already live in, so their real paths carry the
// /back-office prefix. The reports screen is routed at /bo/reports by the IA and does the same. And every /admin screen -
// SCR-700, 724, 715, 710 to 712, and 720 - keeps this app's /back-office prefix.
//
// SCR-600's path is the one that MATCHES: the specification's own path and this app's URL space agree, so there is no prefix
// disagreement to report there. It sits under the BACK-OFFICE layout, because ministry_viewer is staff rather than a supplier
// and that layout is what already refuses a supplier-scoped session with a 403.
//
// SCR-715 is FLATTENED rather than nested: the inventory writes it as /admin/notifications/templates, and /back-office/
// notifications is already this app's notification INBOX - nesting an admin editor under a persona's own inbox route would make
// the two read as the same feature.
//
// TWO ROUTES HAVE NO SCR ID. The reports screen has no specification at all, and an invented id would corrupt an inventory that
// the specifications, backlog and tests all cross-reference.
//
// THREE SCREENS ARE MOUNTED UNDER BOTH SHELLS rather than once, because the two shells are two different URL spaces and a
// back-office user has no route under the supplier layout at all. SCR-900's notification centre is one: the inventory names one
// path, and this is the same SCREEN reached through each persona's own shell, which is the closest the router can come to that
// without giving staff a supplier chrome. SCR-901 is another, listed for "all authenticated", and a supplier reads it for the
// same reason staff do - they receive invitations and award offers, and it is the screen that says which of those they cannot
// switch off. SCR-902 is the third: it is "all authenticated" and existed only under the supplier shell, so a procurement
// officer, evaluator, reviewer or admin had no way to change their own password, enrol MFA, see their sessions or fix their own
// name. The SAME page is mounted rather than a second one written for staff, because every card on it is about the caller's own
// account and the one supplier-scoped card gates itself on being a supplier. SCR-907's help page is mounted in both for a
// different reason: so it keeps the nav the reader came from, since a help link that drops a procurement officer into the
// supplier chrome is worse than no link.
//
// SCR-908 is OUTSIDE every authenticated layout on purpose: the moment a user most needs to say which build they are on is when
// they cannot sign in.
//
// TWO STATIC SEGMENTS ARE DECLARED BEFORE THE PARAMETERISED ROUTE THEY SIT BESIDE, because TanStack matches the literal before
// the parameter and the alternative is a literal read as a reference code: SCR-307's /review/suppliers against
// /review/$referenceCode, and SCR-602's tender monitor against its own detail route.
//
// SCR-906's SEARCH IS NOT GATED in the router: what a caller may find is decided per entity kind on the server, and a
// route-level permission would either lock out someone who can legitimately search one kind or admit someone who can search
// none. Its query lives in the URL so the top bar can submit into the screen, and so a search can be linked to, reloaded and
// gone back to - it was local state, which is why the only way to reach a result was to arrive at an empty page and type again.
//
// FOUR ROUTES CARRY A DECISION ABOUT WHERE THEY SIT. SCR-121's profile is the supplier's own read of their own profile, which
// existed as an endpoint since EPIC-01 and was rendered only by the REVIEWER's screen; SCR-122 to 126's entry points live on
// it, linking to the editors that already exist rather than growing second copies of them. SCR-130's documents centre carries
// SCR-131, 132 and 133 with it, because SCR-133 is a FILTER on that page rather than its own route - "needs attention" is a
// view of the same list, and a second screen would be a second place for "expiring" to be defined. SCR-150's proposals list is
// the missing INDEX: SCR-154's read, SCR-155's revise, SCR-156's withdraw and SCR-157's award response all already live in the
// proposal workspace on the RFQ, and each row links back into it rather than duplicating any of them. And SCR-430 and SCR-431
// share one route, because the detail is a panel rather than a navigation - for the same reason the comparison matrix is a
// matrix.
//
// THE TENDER'S SIX VIEWS are six routes under one reference code. The Suppliers tab is who was asked to bid and what they asked
// back. The Settings tab is everything done TO a tender rather than what the tender is - reassignment, the deadline, addenda
// and cancellation - which the workspace used to stack below the tender's own contents, so reading a line item meant scrolling
// past a cancel button. And SCR-501's brief sits BESIDE the scoring screen rather than inside it: an evaluator reads the brief
// before they start and returns to it when a criterion is ambiguous, and folding it into the form would put a wall of
// instruction in front of the score fields every time.

import { lazy, Suspense } from 'react'
import { PageOutlet } from './components/PageOutlet'

const AdminOverviewPage = lazyRouteComponent(() => import('./routes/admin/AdminOverviewPage'), 'AdminOverviewPage')
const SystemSettingsPage = lazyRouteComponent(() => import('./routes/admin/SystemSettingsPage'), 'SystemSettingsPage')
const ApiKeysPage = lazyRouteComponent(() => import('./routes/admin/ApiKeysPage'), 'ApiKeysPage')
const NotificationTemplatesPage = lazyRouteComponent(() => import('./routes/admin/NotificationTemplatesPage'), 'NotificationTemplatesPage')
const ProfilePage = lazyRouteComponent(() => import('./routes/ProfilePage'), 'ProfilePage')
const DocumentsPage = lazyRouteComponent(() => import('./routes/DocumentsPage'), 'DocumentsPage')
const MyProposalsPage = lazyRouteComponent(() => import('./routes/MyProposalsPage'), 'MyProposalsPage')
const AboutPage = lazyRouteComponent(() => import('./routes/AboutPage'), 'AboutPage')
const HelpPage = lazyRouteComponent(() => import('./routes/HelpPage'), 'HelpPage')
const ReferenceDataPage = lazyRouteComponent(() => import('./routes/admin/ReferenceDataPage'), 'ReferenceDataPage')
const OperationsPage = lazyRouteComponent(() => import('./routes/admin/OperationsPage'), 'OperationsPage')
const UiStringsPage = lazyRouteComponent(() => import('./routes/admin/UiStringsPage'), 'UiStringsPage')
const EmailTemplatesPage = lazyRouteComponent(() => import('./routes/admin/EmailTemplatesPage'), 'EmailTemplatesPage')
const SearchPage = lazyRouteComponent(() => import('./routes/SearchPage'), 'SearchPage')
const AuditExplorerPage = lazyRouteComponent(() => import('./routes/admin/AuditExplorerPage'), 'AuditExplorerPage')
const MinistryOverviewPage = lazyRouteComponent(() => import('./routes/ministry/MinistryOverviewPage'), 'MinistryOverviewPage')
const CategoryCoveragePage = lazyRouteComponent(() => import('./routes/ministry/CategoryCoveragePage'), 'CategoryCoveragePage')
const MinistryRfqMonitorPage = lazyRouteComponent(() => import('./routes/ministry/MinistryRfqMonitorPage'), 'MinistryRfqMonitorPage')
const MinistryRfqDetailRoute = lazyRouteComponent(() => import('./routes/ministry/MinistryRfqDetailPage'), 'MinistryRfqDetailRoute')
const MinistrySupplierRegistryPage = lazyRouteComponent(() => import('./routes/ministry/MinistrySupplierRegistryPage'), 'MinistrySupplierRegistryPage')
const MinistryAwardAnalyticsPage = lazyRouteComponent(() => import('./routes/ministry/MinistryAwardAnalyticsPage'), 'MinistryAwardAnalyticsPage')
const ReportsPage = lazyRouteComponent(() => import('./routes/back-office/ReportsPage'), 'ReportsPage')
const HomePage = lazy(() => import('./routes/HomePage').then((m) => ({ default: m.HomePage })))
const LoginPage = lazyRouteComponent(() => import('./routes/LoginPage'), 'LoginPage')
const RegisterPage = lazyRouteComponent(() => import('./routes/RegisterPage'), 'RegisterPage')
const ForgotPasswordPage = lazyRouteComponent(() => import('./routes/ForgotPasswordPage'), 'ForgotPasswordPage')
const ResetPasswordPage = lazyRouteComponent(() => import('./routes/ResetPasswordPage'), 'ResetPasswordPage')
const VerifyEmailPage = lazyRouteComponent(() => import('./routes/VerifyEmailPage'), 'VerifyEmailPage')
const AcceptTeamInvitePage = lazyRouteComponent(() => import('./routes/AcceptTeamInvitePage'), 'AcceptTeamInvitePage')
const AcceptStaffInvitePage = lazyRouteComponent(() => import('./routes/AcceptStaffInvitePage'), 'AcceptStaffInvitePage')
const SupplierDashboardPage = lazyRouteComponent(() => import('./routes/SupplierDashboardPage'), 'SupplierDashboardPage')
const OnboardingPage = lazyRouteComponent(() => import('./routes/OnboardingPage'), 'OnboardingPage')
const ContactsPage = lazyRouteComponent(() => import('./routes/onboarding/ContactsPage'), 'ContactsPage')
const AddressesPage = lazyRouteComponent(() => import('./routes/onboarding/AddressesPage'), 'AddressesPage')
const BankingPage = lazyRouteComponent(() => import('./routes/onboarding/BankingPage'), 'BankingPage')
const OfferingsPage = lazyRouteComponent(() => import('./routes/onboarding/OfferingsPage'), 'OfferingsPage')
const TeamPage = lazyRouteComponent(() => import('./routes/TeamPage'), 'TeamPage')
const OfferingCatalogPage = lazyRouteComponent(() => import('./routes/OfferingCatalogPage'), 'OfferingCatalogPage')
const SettingsPage = lazyRouteComponent(() => import('./routes/SettingsPage'), 'SettingsPage')
const NotificationPreferencesPage = lazyRouteComponent(() => import('./routes/NotificationPreferencesPage'), 'NotificationPreferencesPage')
const NotificationsPage = lazyRouteComponent(() => import('./routes/NotificationsPage'), 'NotificationsPage')
const EvaluationDashboardPage = lazyRouteComponent(() => import('./routes/EvaluationDashboardPage'), 'EvaluationDashboardPage')
const ProcurementDashboardPage = lazyRouteComponent(() => import('./routes/back-office/ProcurementDashboardPage'), 'ProcurementDashboardPage')
const ApprovalQueuesPage = lazyRouteComponent(() => import('./routes/back-office/ApprovalQueuesPage'), 'ApprovalQueuesPage')
const ReviewDashboardPage = lazyRouteComponent(() => import('./routes/back-office/ReviewDashboardPage'), 'ReviewDashboardPage')
const BackOfficeDashboardPage = lazyRouteComponent(() => import('./routes/BackOfficeDashboardPage'), 'BackOfficeDashboardPage')
const ReviewQueuePage = lazyRouteComponent(() => import('./routes/ReviewQueuePage'), 'ReviewQueuePage')
const ReviewApplicationPage = lazyRouteComponent(() => import('./routes/ReviewApplicationPage'), 'ReviewApplicationPage')
const ComplianceDirectoryPage = lazyRouteComponent(() => import('./routes/ComplianceDirectoryPage'), 'ComplianceDirectoryPage')
const SupplierDirectoryPage = lazyRouteComponent(() => import('./routes/back-office/SupplierDirectoryPage'), 'SupplierDirectoryPage')
const OrganizationsPage = lazyRouteComponent(() => import('./routes/back-office/OrganizationsPage'), 'OrganizationsPage')
const StaffPage = lazyRouteComponent(() => import('./routes/back-office/StaffPage'), 'StaffPage')
const RolesPage = lazyRouteComponent(() => import('./routes/back-office/RolesPage'), 'RolesPage')
const OfferingSearchPage = lazyRouteComponent(() => import('./routes/back-office/OfferingSearchPage'), 'OfferingSearchPage')
const EvaluationTemplatesPage = lazyRouteComponent(() => import('./routes/back-office/EvaluationTemplatesPage'), 'EvaluationTemplatesPage')
const RfqListPage = lazyRouteComponent(() => import('./routes/back-office/RfqListPage'), 'RfqListPage')
const RfqDetailPage = lazyRouteComponent(() => import('./routes/back-office/RfqDetailPage'), 'RfqDetailPage')
const TenderSuppliersPage = lazyRouteComponent(() => import('./routes/back-office/TenderSuppliersPage'), 'TenderSuppliersPage')
const TenderSettingsPage = lazyRouteComponent(() => import('./routes/back-office/TenderSettingsPage'), 'TenderSettingsPage')
const MyEvaluationPage = lazyRouteComponent(() => import('./routes/back-office/MyEvaluationPage'), 'MyEvaluationPage')
const MyEvaluationBriefRoute = lazyRouteComponent(() => import('./routes/back-office/MyEvaluationBriefPage'), 'MyEvaluationBriefRoute')
const ComparisonPage = lazyRouteComponent(() => import('./routes/back-office/ComparisonPage'), 'ComparisonPage')
const ReceivedProposalsPage = lazyRouteComponent(() => import('./routes/back-office/ReceivedProposalsPage'), 'ReceivedProposalsPage')
const AwardPage = lazyRouteComponent(() => import('./routes/back-office/AwardPage'), 'AwardPage')
const SupplierRfqListPage = lazyRouteComponent(() => import('./routes/SupplierRfqListPage'), 'SupplierRfqListPage')
const SupplierRfqDetailPage = lazyRouteComponent(() => import('./routes/SupplierRfqDetailPage'), 'SupplierRfqDetailPage')
const SupplierProposalPage = lazyRouteComponent(() => import('./routes/SupplierProposalPage'), 'SupplierProposalPage')
const SupplierShell = lazy(() => import('./shells/SupplierShell').then((m) => ({ default: m.SupplierShell })))
const BackOfficeShell = lazy(() => import('./shells/BackOfficeShell').then((m) => ({ default: m.BackOfficeShell })))
import { createRootRoute, createRoute, createRouter, lazyRouteComponent, Link, Outlet, redirect } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { LanguageSwitch } from './components/LanguageSwitch'
import { ErrorBoundaryScreen } from './components/ErrorBoundaryScreen'
import { SkipLink } from './components/SkipLink'
import { useAuthStore } from './lib/authStore'
import i18n from 'i18next'
import { SessionExpiredOverlay } from './components/SessionExpiredOverlay'
import { MaintenanceBanner } from './components/MaintenanceBanner'
import { PublicFooter } from './components/PublicFooter'
import { FirstRunLocale } from './components/FirstRunLocale'
import { refresh, getAccount } from './api/auth'


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
      <SkipLink />
      <MaintenanceBanner />
      <Outlet />
      <PublicFooter />
      <SessionExpiredOverlay />
      <FirstRunLocale />
    </Suspense>
  ),
  notFoundComponent: () => <ErrorBoundaryScreen code="404" />,
  errorComponent: () => <ErrorBoundaryScreen code="500" />,
})

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
            style={{ backgroundColor: 'var(--color-brand-solid)', color: 'var(--color-on-brand)' }}
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

const supplierLayoutRoute = createRoute({
  getParentRoute: () => rootRoute,
  id: 'supplier-layout',
  beforeLoad: async () => ensureAuthenticated('/dashboard'),
  component: () => {
    const claims = useAuthStore.getState().claims
    if (!claims?.supplierId) {
      return <ErrorBoundaryScreen code="403" />
    }
    return (
      <SupplierShell>
        <PageOutlet />
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

const profileRoute = createRoute({
  getParentRoute: () => supplierLayoutRoute,
  path: '/profile',
  component: ProfilePage,
})

const documentsRoute = createRoute({
  getParentRoute: () => supplierLayoutRoute,
  path: '/documents',
  component: DocumentsPage,
})

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

const evaluatorLayoutRoute = createRoute({
  getParentRoute: () => rootRoute,
  id: 'evaluator-layout',
  beforeLoad: async () => ensureAuthenticated('/evaluation'),
  component: () => {
    const claims = useAuthStore.getState().claims
    if (claims?.supplierId) {
      return <ErrorBoundaryScreen code="403" />
    }
    return (
      <BackOfficeShell>
        <PageOutlet />
      </BackOfficeShell>
    )
  },
})

const evaluationDashboardRoute = createRoute({
  getParentRoute: () => evaluatorLayoutRoute,
  path: '/evaluation',
  component: EvaluationDashboardPage,
})

const procurementDashboardRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/procurement',
  component: ProcurementDashboardPage,
})

const reportsRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/reports',
  component: ReportsPage,
})

const ministryOverviewRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/ministry',
  component: MinistryOverviewPage,
})

const categoryCoverageRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/ministry/categories',
  component: CategoryCoveragePage,
})

const ministryRfqMonitorRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/ministry/rfqs',
  component: MinistryRfqMonitorPage,
})

const ministryRfqDetailRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/ministry/rfqs/$referenceCode',
  component: MinistryRfqDetailRoute,
})

const ministrySupplierRegistryRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/ministry/suppliers',
  component: MinistrySupplierRegistryPage,
})

const ministryAwardAnalyticsRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/ministry/awards',
  component: MinistryAwardAnalyticsPage,
})

const adminOverviewRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/admin',
  component: AdminOverviewPage,
})

const systemSettingsRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/settings',
  component: SystemSettingsPage,
})

const apiKeysRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/api-keys',
  component: ApiKeysPage,
})

const notificationTemplatesRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/notification-templates',
  component: NotificationTemplatesPage,
})

const referenceDataRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/reference',
  component: ReferenceDataPage,
})

const auditExplorerRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/audit',
  component: AuditExplorerPage,
})

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

const operationsRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/operations',
  component: OperationsPage,
})

const uiStringsRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/ui-strings',
  component: UiStringsPage,
})

const searchRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/search',
  validateSearch: (search: Record<string, unknown>): { q?: string } => ({
    q: typeof search.q === 'string' && search.q.trim() !== '' ? search.q : undefined,
  }),
  component: SearchPage,
})

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
        <PageOutlet />
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

const tenderSuppliersRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/rfqs/$referenceCode/suppliers',
  component: TenderSuppliersPage,
})

const tenderSettingsRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/rfqs/$referenceCode/settings',
  component: TenderSettingsPage,
})

const myEvaluationRoute = createRoute({
  getParentRoute: () => backOfficeLayoutRoute,
  path: '/rfqs/$referenceCode/my-evaluation',
  component: MyEvaluationPage,
})

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
  backOfficeLayoutRoute.addChildren([adminOverviewRoute, systemSettingsRoute, apiKeysRoute, notificationTemplatesRoute, referenceDataRoute, auditExplorerRoute, ministryOverviewRoute, categoryCoverageRoute, ministryRfqMonitorRoute, ministryRfqDetailRoute, ministrySupplierRegistryRoute, ministryAwardAnalyticsRoute, reportsRoute, procurementDashboardRoute, approvalQueuesRoute, reviewDashboardRoute, backOfficeNotificationsRoute, backOfficeAccountRoute, backOfficeNotificationPreferencesRoute, backOfficeHelpRoute, operationsRoute, uiStringsRoute, searchRoute, emailTemplatesRoute, backOfficeDashboardRoute, reviewQueueRoute, complianceDirectoryRoute, reviewApplicationRoute, supplierDirectoryRoute, organizationsRoute, staffRoute, rolesRoute, offeringSearchRoute, evaluationTemplatesRoute, rfqListRoute, myEvaluationRoute, myEvaluationBriefRoute, comparisonRoute, awardRoute, receivedProposalsRoute, tenderSuppliersRoute, tenderSettingsRoute, rfqDetailRoute]),
])

export const router = createRouter({
  routeTree,
  defaultPreload: 'intent',
  defaultNotFoundComponent: () => <ErrorBoundaryScreen code="404" />,
  defaultErrorComponent: () => <ErrorBoundaryScreen code="500" />,
})

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router
  }
}
