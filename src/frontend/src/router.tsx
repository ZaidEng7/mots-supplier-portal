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

const routeTree = rootRoute.addChildren([
  indexRoute,
  loginRoute,
  registerRoute,
  forgotPasswordRoute,
  resetPasswordRoute,
  verifyEmailRoute,
  acceptTeamInviteRoute,
  acceptStaffInviteRoute,
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
    supplierRfqListRoute,
    supplierRfqDetailRoute,
    supplierProposalRoute,
  ]),
  backOfficeLayoutRoute.addChildren([backOfficeDashboardRoute, reviewQueueRoute, reviewApplicationRoute, organizationsRoute, staffRoute, rolesRoute, offeringSearchRoute, evaluationTemplatesRoute, rfqListRoute, myEvaluationRoute, rfqDetailRoute]),
])

export const router = createRouter({ routeTree, defaultNotFoundComponent: () => <ErrorBoundaryScreen code="404" /> })

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router
  }
}
