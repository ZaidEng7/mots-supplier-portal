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

/** DESIGN-SYSTEM.md §5.5: bottom tab bar (max 5 items) for the supplier persona, shown ≤`md`
 * (768px, RESPONSIVE-AND-RTL.md §1) in place of the header's inline nav links, which otherwise
 * overflow at phone widths. EPIC-08: RFQs is the 5th destination the original 4-tab build left
 * room for - this is now at the documented 5-item cap; a 6th destination moves the excess under
 * a "More" sheet per the same spec, not built yet since nothing exceeds 5. Active item matched by
 * path prefix so /onboarding/contacts etc. still highlight "Complete Profile". */
export function MobileTabBar() {
  const { t } = useTranslation()
  const pathname = useRouterState({ select: (s) => s.location.pathname })

  return (
    <nav
      aria-label={t('nav.mobileTabBarLabel')}
      className="fixed inset-x-0 bottom-0 z-30 flex md:hidden"
      style={{ backgroundColor: 'var(--color-bg-surface)', borderTop: '1px solid var(--color-border)' }}
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
