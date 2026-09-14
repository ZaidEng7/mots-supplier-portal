// DESIGN-SYSTEM.md §5.5's bottom tab bar, at most five items, for the supplier persona. It is shown at or below `md` -
// 768px, per RESPONSIVE-AND-RTL.md §1 - in place of the header's inline nav links, which otherwise overflow at phone
// widths.
//
// EPIC-08 made RFQs the fifth destination the original four-tab build left room for, so this is now at the documented
// five-item cap; a sixth destination moves the excess under a "More" sheet per the same spec, which is not built yet
// because nothing exceeds five.
//
// The active item is matched by path prefix, so /onboarding/contacts and its siblings still highlight "Complete
// Profile".

import { useTranslation } from 'react-i18next'
import { Link, useRouterState } from '@tanstack/react-router'
import { LayoutDashboard, ClipboardList, Users, Settings, FileText } from 'lucide-react'

const TABS = [
  { path: '/dashboard', key: 'dashboard', Icon: LayoutDashboard },
  { path: '/onboarding', key: 'onboarding', Icon: ClipboardList },
  { path: '/rfqs', key: 'rfqs', Icon: FileText },
  { path: '/team', key: 'team', Icon: Users },
  { path: '/settings', key: 'settings', Icon: Settings },
] as const

export function MobileTabBar() {
  const { t } = useTranslation()
  const pathname = useRouterState({ select: (s) => s.location.pathname })

  return (
    <nav
      aria-label={t('nav.mobileTabBarLabel')}
      className="fixed inset-x-0 bottom-0 flex md:hidden"
      style={{
        zIndex: 'var(--z-sticky)',
        backgroundColor: 'var(--color-bg-surface)',
        borderTop: '1px solid var(--color-border)',
      }}
    >
      {TABS.map(({ path, key, Icon }) => {
        const active = path === '/dashboard' ? pathname === path : pathname.startsWith(path)
        return (
          <Link
            key={path}
            to={path}
            className="flex flex-1 flex-col items-center gap-1 py-2 text-[length:var(--text-caption)]"
            style={{ color: active ? 'var(--color-text-brand)' : 'var(--color-text-secondary)' }}
          >
            <Icon size={20} aria-hidden="true" />
            {t(`nav.${key}`)}
          </Link>
        )
      })}
    </nav>
  )
}
