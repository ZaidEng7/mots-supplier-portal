import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from '@tanstack/react-router'
import { LanguageSwitch } from '../components/LanguageSwitch'
import { Button } from '../components/ui'
import { useAuthStore } from '../lib/authStore'
import { logout as apiLogout } from '../api/auth'

interface Props {
  children: ReactNode
}

/** Supplier-facing app shell: nav + top bar, distinct from the back-office shell (docs/backlog gap item 2). */
export function SupplierShell({ children }: Props) {
  const { t } = useTranslation()
  const clearSession = useAuthStore((s) => s.clearSession)

  const handleLogout = async () => {
    await apiLogout()
    clearSession()
    window.location.href = '/login'
  }

  return (
    <div className="flex min-h-screen flex-col" style={{ backgroundColor: 'var(--color-bg-app)' }}>
      <header
        className="flex items-center justify-between border-b px-6 py-4"
        style={{ borderColor: 'var(--color-border)', backgroundColor: 'var(--color-bg-surface)' }}
      >
        <div className="flex items-center gap-6">
          <span className="text-lg font-semibold" style={{ color: 'var(--color-text-brand)' }}>
            {t('appName')}
          </span>
          <nav className="flex gap-4">
            <Link to="/dashboard" className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
              {t('nav.dashboard')}
            </Link>
            <Link to="/onboarding" className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
              {t('nav.onboarding')}
            </Link>
            <Link to="/team" className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
              {t('nav.team')}
            </Link>
            <Link to="/settings" className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
              {t('nav.settings')}
            </Link>
          </nav>
        </div>
        <div className="flex items-center gap-3">
          <LanguageSwitch />
          <Button variant="ghost" size="sm" onClick={handleLogout}>
            {t('nav.logout')}
          </Button>
        </div>
      </header>
      <main className="flex flex-1 flex-col px-6 py-8">{children}</main>
    </div>
  )
}
