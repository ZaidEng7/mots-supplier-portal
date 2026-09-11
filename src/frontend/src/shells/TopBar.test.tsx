import { describe, expect, it, vi } from 'vitest'
import type { ReactNode } from 'react'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { List } from 'lucide-react'
import type { NavGroup, NavItem } from './navigation'

/** What the search field asked the router to do, which is the observable half of a submit. */
const navigated: Array<Record<string, unknown>> = []

vi.mock('@tanstack/react-router', () => ({
  Link: ({ to, children, ...rest }: { to: string; children: ReactNode }) => <a href={to} {...rest}>{children}</a>,
  useNavigate: () => (options: Record<string, unknown>) => { navigated.push(options) },
}))
vi.mock('../components/NotificationBell', () => ({ NotificationBell: () => <button type="button">bell</button> }))
vi.mock('../components/LanguageSwitch', () => ({ LanguageSwitch: () => <button type="button">language</button> }))

const { TopBar, breadcrumb } = await import('./TopBar')

const GROUPS: NavGroup[] = [
  {
    headingKey: 'nav.groupSuppliers',
    items: [
      { to: '/back-office/review', labelKey: 'review.title', icon: List, exact: true },
      { to: '/back-office/review/suppliers', labelKey: 'complianceDirectory.title', icon: List },
    ],
  },
]
const CHROME: NavItem[] = [{ to: '/back-office/help', labelKey: 'help.title', icon: List }]
const HOME = { to: '/back-office/dashboard', label: 'nav.dashboard' }
const CONTEXT = { can: () => true, inABuyingBody: true }

const renderBar = (pathname: string, searchTo?: string) => {
  navigated.length = 0
  return render(
    <TopBar
      groups={GROUPS} chrome={CHROME} context={CONTEXT} pathname={pathname}
      home={HOME} searchTo={searchTo} onLogout={() => {}}
    />,
  )
}

describe('the top bar says where you are', () => {
  it('names the section, and lets the page name itself', () => {
    renderBar('/back-office/review/suppliers')

    const trail = screen.getByRole('navigation', { name: 'nav.breadcrumb' })
    expect(trail).toHaveTextContent('nav.dashboard')
    expect(trail).toHaveTextContent('complianceDirectory.title')
  })

  /**
   * Two rows match this path by prefix, and the crumb has to pick the one that owns it. Longest wins,
   * which is the same rule that keeps a reference code out of the trail: the leaf is the page's heading.
   */
  it('picks the deepest section that matches, not the first', () => {
    expect(breadcrumb(GROUPS, '/back-office/review/suppliers')?.to).toBe('/back-office/review/suppliers')
    expect(breadcrumb(GROUPS, '/back-office/review')?.to).toBe('/back-office/review')
  })

  it('shows the trail alone on a page no section owns, rather than an empty separator', () => {
    renderBar('/back-office/somewhere-else')

    expect(breadcrumb(GROUPS, '/back-office/somewhere-else')).toBeNull()
    expect(screen.getByRole('navigation', { name: 'nav.breadcrumb' })).toHaveTextContent('nav.dashboard')
  })

  it('offers search only where the shell has somewhere to search', () => {
    const { rerender } = renderBar('/back-office/review')
    expect(screen.queryByRole('searchbox', { name: 'search.title' })).toBeNull()

    rerender(
      <TopBar
        groups={GROUPS} chrome={CHROME} context={CONTEXT} pathname="/back-office/review"
        home={HOME} searchTo="/back-office/search" onLogout={() => {}}
      />,
    )
    expect(screen.getByRole('searchbox', { name: 'search.title' })).toBeInTheDocument()
  })

  /**
   * The control a reader can see is the control they get.
   *
   * <p>This was a `Link` 260 pixels wide, on the page background, inside an input border, with a
   * magnifier and the word "Search" in it - a text field in every respect a reader can perceive and in
   * none that they can use. Clicking navigated; typing did nothing; the destination was a page whose
   * only content was the box they had just tried to type in.</p>
   */
  it('is a field that submits, not a link wearing the clothes of one', async () => {
    renderBar('/back-office/review', '/back-office/search')

    const box = screen.getByRole('searchbox', { name: 'search.title' })
    expect(box.tagName).toBe('INPUT')
    expect(box.closest('a')).toBeNull()

    await userEvent.type(box, 'catering{Enter}')

    expect(navigated).toEqual([{ to: '/back-office/search', search: { q: 'catering' } }])
  })

  it('does not submit an empty search', async () => {
    renderBar('/back-office/review', '/back-office/search')

    await userEvent.type(screen.getByRole('searchbox', { name: 'search.title' }), '   {Enter}')

    expect(navigated).toEqual([])
  })
})

/**
 * The sidebar is 260px and the reflow floor is a 320px viewport, so at that width the bar carries the
 * navigation instead. A disclosure rather than an overlay: no focus trap, no scroll lock, no escape key.
 */
describe('the narrow-viewport disclosure', () => {
  it('starts shut, and says so', () => {
    renderBar('/back-office/review')

    const toggle = screen.getByRole('button', { name: 'nav.primaryLabel' })
    expect(toggle).toHaveAttribute('aria-expanded', 'false')
    expect(toggle).toHaveAttribute('aria-controls')
  })

  it('opens on click and closes when a destination is followed', async () => {
    renderBar('/back-office/review')
    const toggle = screen.getByRole('button', { name: 'nav.primaryLabel' })

    await userEvent.click(toggle)
    expect(toggle).toHaveAttribute('aria-expanded', 'true')

    // Closing on navigation is the whole reason this is a disclosure and not a menu: nothing else
    // dismisses it, so a panel left open would cover the page it just took you to.
    await userEvent.click(screen.getAllByRole('link', { name: 'review.title' })[0])
    expect(toggle).toHaveAttribute('aria-expanded', 'false')
  })

  it('points aria-controls at a panel that is really in the document while shut', () => {
    // A control pointing at nothing is a control that says nothing. The panel is hidden rather than
    // unmounted, and `hidden` rather than moved off-screen, so nothing inside it takes focus.
    renderBar('/back-office/review')
    const panel = document.getElementById(screen.getByRole('button', { name: 'nav.primaryLabel' }).getAttribute('aria-controls') ?? '')

    expect(panel).not.toBeNull()
    expect(panel).toHaveAttribute('hidden')
  })
})
