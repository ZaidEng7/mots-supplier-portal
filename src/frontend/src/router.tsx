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
const SupplierDashboardPage = lazy(() => import('./routes/SupplierDashboardPage').then((m) => ({ default: m.SupplierDashboardPage })))
const OnboardingPage = lazy(() => import('./routes/OnboardingPage').then((m) => ({ default: m.OnboardingPage })))
const SettingsPage = lazy(() => import('./routes/SettingsPage').then((m) => ({ default: m.SettingsPage })))
const BackOfficeDashboardPage = lazy(() => import('./routes/BackOfficeDashboardPage').then((m) => ({ default: m.BackOfficeDashboardPage })))
const ReviewQueuePage = lazy(() => import('./routes/ReviewQueuePage').then((m) => ({ default: m.ReviewQueuePage })))
const ReviewApplicationPage = lazy(() => import('./routes/ReviewApplicationPage').then((m) => ({ default: m.ReviewApplicationPage })))
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

const settingsRoute = createRoute({
  getParentRoute: () => supplierLayoutRoute,
  path: '/settings',
  component: SettingsPage,
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

const routeTree = rootRoute.addChildren([
  indexRoute,
  loginRoute,
  registerRoute,
  forgotPasswordRoute,
  resetPasswordRoute,
  verifyEmailRoute,
  supplierLayoutRoute.addChildren([supplierDashboardRoute, onboardingRoute, settingsRoute]),
  backOfficeLayoutRoute.addChildren([backOfficeDashboardRoute, reviewQueueRoute, reviewApplicationRoute]),
])

export const router = createRouter({ routeTree, defaultNotFoundComponent: () => <ErrorBoundaryScreen code="404" /> })

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router
  }
}
