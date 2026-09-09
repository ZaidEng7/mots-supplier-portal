import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import {Link, useRouterState} from '@tanstack/react-router'
import { LanguageSwitch } from '../components/LanguageSwitch'
import { NotificationBell } from '../components/NotificationBell'
import { ErpStatusBanner } from '../components/ErpStatusBanner'
import { MobileTabBar } from '../components/MobileTabBar'
import { Button } from '../components/ui'
import { useAuthStore } from '../lib/authStore'
import { logout as apiLogout } from '../api/auth'

interface Props {
  children: ReactNode
}

/** Supplier-facing app shell: nav + top bar, distinct from the back-office shell (docs/backlog gap
 * item 2). DESIGN-SYSTEM.md §5.5: below `md` the inline nav links (which overflow the header at
 * phone widths) give way to a fixed MobileTabBar; the top bar (logo/language/logout) stays on
 * every viewport. `main` gets bottom padding on mobile only, so the fixed tab bar never covers
 * the last bit of page content. */
/**
 * One navigation link, with the current page said rather than only coloured.
 *
 * <p>`aria-current="page"` is the half §D3 was missing: the nav had no visible current state at all, and
 * a colour alone would not reach a screen reader. The prefix match is the same rule the mobile tab bar
 * already uses, so /onboarding/contacts still highlights the step it belongs to; `exact` is for the two
 * that are prefixes of other routes.</p>
 */
function SupplierNavLink({ to, label, pathname, exact = false }: Readonly<{
  to: string
  label: string
  pathname: string
  exact?: boolean
}>) {
  const active = exact ? pathname === to : pathname.startsWith(to)
  return (
    <Link
      to={to as never}
      aria-current={active ? 'page' : undefined}
      className="text-[length:var(--density-body)]"
      style={{
        color: active ? 'var(--color-text-brand)' : 'var(--color-text-secondary)',
        fontWeight: active ? 'var(--fw-medium)' : undefined,
      }}
    >
      {label}
    </Link>
  )
}

export function SupplierShell({ children }: Props) {
  const pathname = useRouterState({ select: (state) => state.location.pathname })
  const { t } = useTranslation()
  const clearSession = useAuthStore((s) => s.clearSession)

  const handleLogout = async () => {
    await apiLogout()
    clearSession()
    window.location.href = '/login'
  }

  // `msp-density-supplier` sets --density-body to 16px for everything inside this shell.
  // RECONCILIATION.md's reasoning: this reader is an outside company completing a legal application a
  // few times a year under deadline, not an officer scanning tables all day. The back office keeps the
  // 14px default, so only one of the two shells has to say anything.

  return (
    <div className="msp-density-supplier flex min-h-screen flex-col" style={{ backgroundColor: 'var(--color-bg-app)' }}>
      {/* SCR-045: above the header, so it is chrome rather than page content. */}
      <ErpStatusBanner />
      <header
        className="flex items-center justify-between border-b px-4 py-4 sm:px-6"
        style={{ borderColor: 'var(--color-border)', backgroundColor: 'var(--color-bg-surface)' }}
      >
        <div className="flex items-center gap-6">
          <span className="text-[length:var(--text-h4)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-brand)' }}>
            {t('appName')}
          </span>
          {/*
            Two named groups, not one flat list of eleven links.

            §D3 measured the old shape: twelve top-level links on every screen, "Complete Profile" and
            "Profile" adjacent with nothing to tell them apart, and no visible current page. A supplier
            arriving to bid had to read all twelve to find the two that were their reason for coming.

            Two <nav> elements rather than one with visual dividers, because a screen-reader user gets two
            named landmarks to jump between - the grouping made real rather than only drawn. Account
            chrome (settings, notifications, help) moved to the right cluster, where account chrome
            belongs, and off the path between a supplier and a tender.
          */}
          <nav aria-label={t('nav.groupBidding')} className="hidden items-center gap-4 md:flex">
            <SupplierNavLink to="/dashboard" label={t('nav.dashboard')} pathname={pathname} exact />
            <SupplierNavLink to="/rfqs" label={t('nav.rfqs')} pathname={pathname} />
            {/* SCR-150: "what have I bid on" had no answer short of opening every invitation. */}
            <SupplierNavLink to="/proposals" label={t('nav.proposals')} pathname={pathname} />
          </nav>

          <span aria-hidden="true" className="hidden h-4 w-px md:block" style={{ backgroundColor: 'var(--color-border)' }} />

          <nav aria-label={t('nav.groupCompany')} className="hidden items-center gap-4 md:flex">
            <SupplierNavLink to="/onboarding" label={t('nav.onboarding')} pathname={pathname} />
            {/* SCR-121: the supplier's own profile, which had no surface at all until now. */}
            <SupplierNavLink to="/profile" label={t('nav.profile')} pathname={pathname} />
            {/* SCR-130: documents existed only inside the onboarding wizard. */}
            <SupplierNavLink to="/documents" label={t('nav.documents')} pathname={pathname} />
            <SupplierNavLink to="/offerings" label={t('nav.offerings')} pathname={pathname} />
            <SupplierNavLink to="/team" label={t('nav.team')} pathname={pathname} />
          </nav>
        </div>
        <div className="flex items-center gap-3">
          {/* Account chrome, off the path between a supplier and a tender. SCR-901 is here too: they
              receive invitations and award offers, and this is where they learn which of those arrive
              whatever their preferences say. */}
          <nav aria-label={t('nav.groupAccount')} className="hidden items-center gap-4 md:flex">
            <SupplierNavLink to="/settings" label={t('nav.settings')} pathname={pathname} exact />
            <SupplierNavLink to="/settings/notifications" label={t('notificationPreferences.title')} pathname={pathname} />
            <SupplierNavLink to="/help" label={t('help.title')} pathname={pathname} />
          </nav>
          <NotificationBell />
          <LanguageSwitch />
          <Button variant="ghost" size="sm" onClick={handleLogout}>
            {t('nav.logout')}
          </Button>
        </div>
      </header>
      <main id="main" className="flex flex-1 flex-col px-4 py-8 pb-20 sm:px-6 md:pb-8">{children}</main>
      <MobileTabBar />
    </div>
  )
}
