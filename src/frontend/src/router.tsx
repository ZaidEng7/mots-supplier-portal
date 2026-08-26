import { createRootRoute, createRoute, createRouter, Link, Outlet, redirect } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { HomePage } from './routes/HomePage'
import { LanguageSwitch } from './components/LanguageSwitch'
import { LoginPage } from './routes/LoginPage'
import { ForgotPasswordPage } from './routes/ForgotPasswordPage'
import { ResetPasswordPage } from './routes/ResetPasswordPage'
import { VerifyEmailPage } from './routes/VerifyEmailPage'
import { SupplierDashboardPage } from './routes/SupplierDashboardPage'
import { BackOfficeDashboardPage } from './routes/BackOfficeDashboardPage'
import { SupplierShell } from './shells/SupplierShell'
import { BackOfficeShell } from './shells/BackOfficeShell'
import { ErrorBoundaryScreen } from './components/ErrorBoundaryScreen'
import { useAuthStore } from './lib/authStore'
import { refresh } from './api/auth'

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
  component: () => <Outlet />,
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

const forgotPasswordRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/forgot-password',
  component: ForgotPasswordPage,
})

const resetPasswordRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/reset-password',
  validateSearch: (search: Record<string, unknown>): { userId?: string; token?: string } => ({
    userId: typeof search.userId === 'string' ? search.userId : undefined,
    token: typeof search.token === 'string' ? search.token : undefined,
  }),
  component: ResetPasswordPage,
})

const verifyEmailRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/verify-email',
  validateSearch: (search: Record<string, unknown>): { userId?: string; token?: string } => ({
    userId: typeof search.userId === 'string' ? search.userId : undefined,
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

const routeTree = rootRoute.addChildren([
  indexRoute,
  loginRoute,
  forgotPasswordRoute,
  resetPasswordRoute,
  verifyEmailRoute,
  supplierLayoutRoute.addChildren([supplierDashboardRoute]),
  backOfficeLayoutRoute.addChildren([backOfficeDashboardRoute]),
])

export const router = createRouter({ routeTree, defaultNotFoundComponent: () => <ErrorBoundaryScreen code="404" /> })

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router
  }
}
