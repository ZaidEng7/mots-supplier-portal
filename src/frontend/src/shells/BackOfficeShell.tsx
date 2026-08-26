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

/** Back-office (MOTS staff) app shell: dark sidebar-style top bar, visually distinct from the
 * supplier shell so staff and suppliers are never mistaken for the same surface. */
export function BackOfficeShell({ children }: Props) {
  const { t } = useTranslation()
  const clearSession = useAuthStore((s) => s.clearSession)

  const handleLogout = async () => {
    await apiLogout()
    clearSession()
    window.location.href = '/login'
  }

  return (
    <div className="flex min-h-screen flex-col" style={{ backgroundColor: 'var(--n-900)' }}>
      <header className="flex items-center justify-between border-b px-6 py-4" style={{ borderColor: 'var(--n-700)', backgroundColor: 'var(--n-800)' }}>
        <div className="flex items-center gap-6">
          <span className="text-lg font-semibold" style={{ color: 'var(--accent-gold-500)' }}>
            {t('appName')} · {t('nav.backOffice')}
          </span>
          <nav className="flex gap-4">
            <Link to="/back-office/dashboard" className="text-[length:var(--text-body-sm)]" style={{ color: '#F4F1EC' }}>
              {t('nav.dashboard')}
            </Link>
            <Link to="/back-office/review" className="text-[length:var(--text-body-sm)]" style={{ color: '#F4F1EC' }}>
              {t('review.title')}
            </Link>
          </nav>
        </div>
        <div className="flex items-center gap-3">
          <LanguageSwitch />
          <Button variant="ghost" size="sm" style={{ color: '#F4F1EC' }} onClick={handleLogout}>
            {t('nav.logout')}
          </Button>
        </div>
      </header>
      <main className="flex flex-1 flex-col px-6 py-8" style={{ backgroundColor: 'var(--color-bg-app)' }}>
        {children}
      </main>
    </div>
  )
}
