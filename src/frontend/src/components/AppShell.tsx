import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { LanguageSwitch } from './LanguageSwitch'

interface Props {
  children: ReactNode
}

/** Top bar + nav + content layout shell, RTL-aware (docs/backlog/ROADMAP.md Phase 0 app-shell slice). */
export function AppShell({ children }: Props) {
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
        <LanguageSwitch />
      </header>
      <main className="flex flex-1 flex-col px-6 py-8">{children}</main>
    </div>
  )
}
