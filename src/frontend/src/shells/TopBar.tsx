import { useId, useState } from 'react'
import { Link } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { Menu, Search } from 'lucide-react'
import { Icon } from '../components/ui/Icon'
import { LanguageSwitch } from '../components/LanguageSwitch'
import { NotificationBell } from '../components/NotificationBell'
import { Button } from '../components/ui'
import { NavGroups, isCurrent } from './Sidebar'
import type { NavContext, NavGroup, NavItem } from './navigation'

/**
 * The trail from the shell's own front page to where you are.
 *
 * <p>Two levels, and deliberately not three. The section comes from the navigation list, so it cannot
 * disagree with the sidebar, and the leaf is left to the page's own heading rather than invented here:
 * the third level of most of these paths is a reference code, and a crumb reading "RFQ-2026-000001"
 * tells a reader nothing their heading has not already said.</p>
 */
export function breadcrumb(groups: readonly NavGroup[], pathname: string): NavItem | null {
  const rows = groups.flatMap((group) => group.items)
  const matches = rows.filter((item) => isCurrent(item, pathname))
  // Longest path wins, so /back-office/review/suppliers is Compliance rather than Review queue.
  return matches.sort((a, b) => b.to.length - a.to.length)[0] ?? null
}

export interface TopBarProps {
  groups: readonly NavGroup[]
  chrome: readonly NavItem[]
  context: NavContext
  pathname: string
  /** Where the shell's own front page is, and what it is called. */
  home: { to: string; label: string }
  /** The shell's search destination, where it has one. The supplier side does not. */
  searchTo?: string
  onLogout: () => void
}

/**
 * Context and controls, never navigation - except at narrow widths, where it carries the navigation
 * because the sidebar cannot.
 *
 * <p><b>The narrow case.</b> The sidebar is 260px and the reflow floor is a 320px viewport, so below
 * `md` it is not drawn and this bar grows a disclosure holding the same list. A disclosure rather than
 * an overlay drawer: an overlay needs a focus trap, a scroll lock and an escape key, which is three new
 * behaviours to get right on every screen in the product, and the content simply moving down the page
 * needs none of them. `aria-expanded` and `aria-controls` are the whole interaction.</p>
 */
export function TopBar({ groups, chrome, context, pathname, home, searchTo, onLogout }: Readonly<TopBarProps>) {
  const { t } = useTranslation()
  const [navOpen, setNavOpen] = useState(false)
  const navId = useId()
  const here = breadcrumb(groups, pathname)

  return (
    <>
      <header
        className="sticky top-0 flex flex-wrap items-center gap-x-3.5 gap-y-2 px-4 py-2.5 sm:px-6"
        style={{
          zIndex: 'var(--z-header)',
          backgroundColor: 'var(--color-bg-surface)',
          borderBlockEnd: '1px solid var(--color-border)',
        }}
      >
        <button
          type="button"
          onClick={() => setNavOpen((open) => !open)}
          aria-expanded={navOpen}
          aria-controls={navId}
          className="grid size-8 place-items-center rounded-[var(--radius-sm)] md:hidden"
          style={{ color: 'var(--color-text-secondary)', border: '1px solid var(--color-border)' }}
        >
          <Icon as={Menu} size={18} />
          <span className="sr-only">{t('nav.primaryLabel')}</span>
        </button>

        {/*
          On the shell's own front page the section IS the home crumb, and rendering both read
          "Dashboard / Dashboard" - a trail that says the same word twice tells a reader less than one
          that says it once.
        */}
        <nav aria-label={t('nav.breadcrumb')} className="flex min-w-0 items-center gap-1.5 text-[length:var(--text-body-sm)]">
          {here?.to === home.to ? (
            <span aria-current="page" className="truncate font-[var(--fw-medium)]" style={{ color: 'var(--color-text-primary)' }}>
              {home.label}
            </span>
          ) : (
            <>
              <Link to={home.to as never} className="no-underline" style={{ color: 'var(--color-text-secondary)' }}>
                {home.label}
              </Link>
              {here ? (
                <>
                  <span aria-hidden="true" style={{ color: 'var(--color-text-muted)' }}>/</span>
                  <span aria-current="page" className="truncate font-[var(--fw-medium)]" style={{ color: 'var(--color-text-primary)' }}>
                    {t(here.labelKey)}
                  </span>
                </>
              ) : null}
            </>
          )}
        </nav>

        <div className="ms-auto flex items-center gap-2">
          {searchTo ? (
            <Link
              to={searchTo as never}
              className="hidden items-center gap-2 rounded-[var(--radius-md)] px-2.5 py-1.5 text-[length:var(--text-body-sm)] no-underline lg:flex"
              style={{
                inlineSize: '260px',
                backgroundColor: 'var(--color-bg-app)',
                border: '1px solid var(--color-border-input)',
                color: 'var(--color-text-secondary)',
                whiteSpace: 'nowrap',
              }}
            >
              <Icon as={Search} />
              {t('search.title')}
            </Link>
          ) : null}

          {/*
            Account chrome, off the path between a supplier and a tender. Rendered as text rather than
            bare icons: an icon-only row of three would need three accessible names to say what three
            words already say, and these are read rarely enough that the words cost nothing.
          */}
          <nav aria-label={t('nav.groupAccount')} className="hidden items-center gap-3 lg:flex">
            {chrome.map((item) => (
              <Link
                key={item.to}
                to={item.to as never}
                aria-current={isCurrent(item, pathname) ? 'page' : undefined}
                className="text-[length:var(--text-body-sm)] no-underline"
                style={{
                  color: isCurrent(item, pathname) ? 'var(--color-text-brand)' : 'var(--color-text-secondary)',
                }}
              >
                {t(item.labelKey)}
              </Link>
            ))}
          </nav>

          <NotificationBell />
          <LanguageSwitch />
          <Button variant="ghost" size="sm" onClick={onLogout}>{t('nav.logout')}</Button>
        </div>
      </header>

      {/*
        The same list the sidebar draws, in the frame the sidebar cannot use at this width. It is always
        in the document so `aria-controls` points at something whether it is open or shut, and it is
        `hidden` when shut rather than moved off-screen, so nothing inside it is focusable while
        invisible.
      */}
      <div
        id={navId}
        hidden={!navOpen}
        className="flex flex-col gap-4 px-2.5 py-4 md:hidden"
        style={{ backgroundColor: 'var(--color-field)', color: 'var(--color-on-field)' }}
      >
        <NavGroups groups={groups} context={context} pathname={pathname} onNavigate={() => setNavOpen(false)} />
        <nav aria-label={t('nav.groupAccount')} className="flex flex-col gap-px">
          {chrome.map((item) => (
            <Link
              key={item.to}
              to={item.to as never}
              onClick={() => setNavOpen(false)}
              aria-current={isCurrent(item, pathname) ? 'page' : undefined}
              className="rounded-[var(--radius-sm)] px-2 py-[7px] text-[length:var(--text-body-sm)] no-underline"
              style={{
                color: isCurrent(item, pathname) ? 'var(--color-on-field)' : 'var(--color-on-field-muted)',
                backgroundColor: isCurrent(item, pathname) ? 'var(--color-field-active)' : undefined,
              }}
            >
              {t(item.labelKey)}
            </Link>
          ))}
        </nav>
      </div>
    </>
  )
}
