// Three groups: what the bar says about where you are, whether it offers a way back up, and the narrow-viewport
// disclosure.
//
// The search assertions read what the field asked the ROUTER to do, which is the observable half of a submit.
//
// THE TRAIL names the section and lets the page name itself. It picks the deepest section that matches rather than the
// first: two rows match that path by prefix and the crumb has to pick the one that owns it, which is the same rule that
// keeps a reference code out of the trail - the leaf is the page's heading. On a page no section owns it shows the trail
// alone rather than an empty separator.
//
// THE WAY UP is the defect these tests were extended for. The section crumb was a span marked aria-current on every path
// it matched, so a page below a section announced the section as the page you were on and offered no link back to it.
// It was chosen from every row, including rows the account cannot see, so an evaluator scoring a tender was told they
// were in Tenders. And an `exact` row disowned the pages under it, so a review record at /back-office/review/SUP-1 had
// no section at all and the trail was a lone Dashboard. The tests read the REAL back-office and supplier navigation rather
// than a two-row fixture, because the defect lived in which rows exist and how they are flagged. The denominator is
// every row of both lists, thirty-six today, asserted before the rule. Every row is rendered twice with every permission
// granted: one level below its path the trail links the owner breadcrumb() names and marks nothing as the current page,
// and at its own path the section is the current page and is not a link. Each row is its own case rather than one loop:
// the loop rendered seventy-two bars inside a single test, took three and a half seconds locally under coverage, and
// timed out at five on the CI runner, where the budget is per test. Split, a failure also names the row that broke.
// Link is a plain anchor in this file, so these
// cases say what the bar itself marks and cannot see the mark TanStack's Link adds to a link that matches by prefix. That
// mark kept the crumb announced as the current page after it became a link, and currentPageMarks.test renders the real
// Link to catch it.
//
// THE PERSONA is the evaluator, who holds evaluation.score, evaluation.submit and rfq.clarify and nothing else. On their
// scoring screen and their brief the trail links My evaluations, the list they opened the tender from, and never names
// Tenders. On the comparison, which no row of theirs claims, it names no section. The control is a session holding
// rfq.read on that same comparison, whose trail does link Tenders - so the evaluator's missing crumb is the visibility
// filter at work and not a row that does not exist. The rail follows the same rule: an account that sees both rows gets
// My evaluations lit and Tenders dark, read from the disclosure's copy of the list.
//
// THE MATCHER CONTROL runs the link check on the markup the trail used to render, a span marked as the current page, and
// expects it to find no section link; and on a twin whose section is a real link, and expects it found. A check that
// cannot tell the two apart would pass the rule above in silence.
//
// Search is offered only where the shell has somewhere to search. And the control a reader can see is the control they
// get: this was a Link 260 pixels wide, on the page background, inside an input border, with a magnifier and the word
// "Search" in it - a text field in every respect a reader can perceive and in none that they can use. Clicking
// navigated; typing did nothing; the destination was a page whose only content was the box they had just tried to type
// in. So one test is that it is a field that submits rather than a link wearing the clothes of one, and the next is that
// an empty search does not submit.
//
// THE DISCLOSURE exists because the sidebar is 260px and the reflow floor is a 320px viewport, so at that width the bar
// carries the navigation instead - a disclosure rather than an overlay: no focus trap, no scroll lock, no escape key. It
// starts shut and says so; it opens on click and closes when a destination is followed, which is the whole reason it is
// a disclosure and not a menu, because nothing else dismisses it and a panel left open would cover the page it just
// took you to; and aria-controls points at a panel that is really in the document while shut, because a control pointing
// at nothing is a control that says nothing - the panel is hidden rather than unmounted, and `hidden` rather than moved
// off-screen, so nothing inside it takes focus.

import { describe, expect, it, vi } from 'vitest'
import type { ReactNode } from 'react'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { BACK_OFFICE_CHROME, BACK_OFFICE_NAV, SUPPLIER_CHROME, SUPPLIER_NAV } from './navigation'
import type { NavContext, NavGroup, NavItem } from './navigation'

const navigated: Array<Record<string, unknown>> = []

vi.mock('@tanstack/react-router', () => ({
  Link: ({ to, children, ...rest }: { to: string; children: ReactNode }) => <a href={to} {...rest}>{children}</a>,
  useNavigate: () => (options: Record<string, unknown>) => { navigated.push(options) },
}))
vi.mock('../components/NotificationBell', () => ({ NotificationBell: () => <button type="button">bell</button> }))
vi.mock('../components/LanguageSwitch', () => ({ LanguageSwitch: () => <button type="button">language</button> }))

const { TopBar, breadcrumb } = await import('./TopBar')
const { visibleGroups } = await import('./Sidebar')

interface Shell {
  groups: readonly NavGroup[]
  chrome: readonly NavItem[]
  home: { to: string; label: string }
}

const BACK_OFFICE: Shell = {
  groups: BACK_OFFICE_NAV, chrome: BACK_OFFICE_CHROME, home: { to: '/back-office/dashboard', label: 'nav.dashboard' },
}
const SUPPLIER: Shell = { groups: SUPPLIER_NAV, chrome: SUPPLIER_CHROME, home: { to: '/dashboard', label: 'nav.dashboard' } }

const EVERYTHING: NavContext = { can: () => true, inABuyingBody: true }
const holding = (...permissions: string[]): NavContext => ({
  can: (permission) => permissions.includes(permission),
  inABuyingBody: true,
})
const EVALUATOR = holding('evaluation.score', 'evaluation.submit', 'rfq.clarify')

const renderBar = (
  pathname: string,
  { shell = BACK_OFFICE, context = EVERYTHING, searchTo }: { shell?: Shell; context?: NavContext; searchTo?: string } = {},
) => {
  navigated.length = 0
  return render(
    <TopBar
      groups={shell.groups} chrome={shell.chrome} context={context} pathname={pathname}
      home={shell.home} searchTo={searchTo} onLogout={() => {}}
    />,
  )
}

const theTrail = () => screen.getByRole('navigation', { name: 'nav.breadcrumb' })

const sectionLink = (trail: HTMLElement, owner: Pick<NavItem, 'to' | 'labelKey'>) =>
  within(trail).queryAllByRole('link').find((a) => a.getAttribute('href') === owner.to && a.textContent === owner.labelKey) ?? null

const currentMarks = (trail: HTMLElement) => Array.from(trail.querySelectorAll('[aria-current]'))

describe('the top bar says where you are', () => {
  it('names the section, and lets the page name itself', () => {
    renderBar('/back-office/review/suppliers')

    expect(theTrail()).toHaveTextContent('nav.dashboard')
    expect(theTrail()).toHaveTextContent('complianceDirectory.title')
  })

  it('picks the deepest section that matches, not the first', () => {
    expect(breadcrumb(BACK_OFFICE_NAV, '/back-office/review/suppliers')?.to).toBe('/back-office/review/suppliers')
    expect(breadcrumb(BACK_OFFICE_NAV, '/back-office/review')?.to).toBe('/back-office/review')
  })

  it('shows the trail alone on a page no section owns, rather than an empty separator', () => {
    renderBar('/back-office/somewhere-else')

    expect(breadcrumb(BACK_OFFICE_NAV, '/back-office/somewhere-else')).toBeNull()
    expect(theTrail()).toHaveTextContent('nav.dashboard')
  })

  it('offers search only where the shell has somewhere to search', () => {
    const { rerender } = renderBar('/back-office/review')
    expect(screen.queryByRole('searchbox', { name: 'search.title' })).toBeNull()

    rerender(
      <TopBar
        groups={BACK_OFFICE.groups} chrome={BACK_OFFICE.chrome} context={EVERYTHING} pathname="/back-office/review"
        home={BACK_OFFICE.home} searchTo="/back-office/search" onLogout={() => {}}
      />,
    )
    expect(screen.getByRole('searchbox', { name: 'search.title' })).toBeInTheDocument()
  })

  it('is a field that submits, not a link wearing the clothes of one', async () => {
    renderBar('/back-office/review', { searchTo: '/back-office/search' })

    const box = screen.getByRole('searchbox', { name: 'search.title' })
    expect(box.tagName).toBe('INPUT')
    expect(box.closest('a')).toBeNull()

    await userEvent.type(box, 'catering{Enter}')

    expect(navigated).toEqual([{ to: '/back-office/search', search: { q: 'catering' } }])
  })

  it('does not submit an empty search', async () => {
    renderBar('/back-office/review', { searchTo: '/back-office/search' })

    await userEvent.type(screen.getByRole('searchbox', { name: 'search.title' }), '   {Enter}')

    expect(navigated).toEqual([])
  })
})

describe('the trail offers a way back up', () => {
  const ROWS = [BACK_OFFICE, SUPPLIER].flatMap((shell) => shell.groups.flatMap((group) => group.items).map((row) => ({ shell, row })))

  it('checks every row of both real navigations', () => {
    expect(ROWS.map(({ row }) => row)).toEqual([...BACK_OFFICE_NAV, ...SUPPLIER_NAV].flatMap((group) => group.items))
    expect(ROWS.length).toBeGreaterThan(30)
  })

  it.each(ROWS)('$row.to/X-1: the trail links its owner and marks nothing as the current page', ({ shell, row }) => {
    const below = `${row.to}/X-1`
    const owner = breadcrumb(visibleGroups(shell.groups, EVERYTHING), below)
    renderBar(below, { shell })

    expect(owner, `${below} has no owner`).not.toBeNull()
    expect(sectionLink(theTrail(), owner!), `${below} does not link ${owner?.to}`).not.toBeNull()
    expect(currentMarks(theTrail()), `${below} marks a crumb as the current page`).toEqual([])
  })

  it.each(ROWS)('$row.to: the section is the current page, and not a link to itself', ({ shell, row }) => {
    renderBar(row.to, { shell })
    const marks = currentMarks(theTrail())

    expect(marks.map((mark) => mark.textContent), `${row.to} is not marked as the current page`).toEqual([row.labelKey])
    expect(marks.some((mark) => mark.tagName === 'A'), `${row.to} marks a link as the current page`).toBe(false)
    expect(
      within(theTrail()).queryAllByRole('link').some((a) => a.getAttribute('href') === row.to),
      `${row.to} links to itself`,
    ).toBe(false)
  })

  it('gives a record below the exact Review queue row to that row, and the compliance directory still to itself', () => {
    expect(breadcrumb(BACK_OFFICE_NAV, '/back-office/review/SUP-1')?.labelKey).toBe('review.title')
    expect(breadcrumb(BACK_OFFICE_NAV, '/back-office/review/suppliers')?.labelKey).toBe('complianceDirectory.title')

    renderBar('/back-office/review/SUP-1')
    expect(sectionLink(theTrail(), { to: '/back-office/review', labelKey: 'review.title' })).not.toBeNull()
  })
})

describe('the trail names only what this account can see', () => {
  it('leads an evaluator back to My evaluations from their scoring screen and brief, and never names Tenders', () => {
    const pages = ['/back-office/rfqs/RFQ-1/my-evaluation', '/back-office/rfqs/RFQ-1/brief']
    expect(pages).toHaveLength(2)

    for (const page of pages) {
      const { unmount } = renderBar(page, { context: EVALUATOR })
      expect(sectionLink(theTrail(), { to: '/evaluation', labelKey: 'evaluationDashboard.title' }), page).not.toBeNull()
      expect(theTrail(), page).not.toHaveTextContent('rfq.title')
      unmount()
    }
  })

  it('names no Tenders crumb to an evaluator on a tender page no row of theirs claims', () => {
    renderBar('/back-office/rfqs/RFQ-1/comparison', { context: EVALUATOR })

    expect(theTrail()).not.toHaveTextContent('rfq.title')
  })

  it('links Tenders on that same page for a session holding rfq.read, so the missing crumb is the filter and not a missing row', () => {
    renderBar('/back-office/rfqs/RFQ-1/comparison', { context: holding('rfq.read') })

    expect(sectionLink(theTrail(), { to: '/back-office/rfqs', labelKey: 'rfq.title' })).not.toBeNull()
  })

  it('lights My evaluations and not Tenders on the rail, for an account that sees both', () => {
    renderBar('/back-office/rfqs/RFQ-1/my-evaluation', { context: holding('rfq.read', 'evaluation.score') })
    const panel = document.getElementById(screen.getByRole('button', { name: 'nav.primaryLabel' }).getAttribute('aria-controls') ?? '')
    expect(panel).not.toBeNull()

    const rows = within(panel as HTMLElement).getAllByRole('link', { hidden: true })
    expect(rows.map((a) => a.textContent)).toEqual(expect.arrayContaining(['rfq.title', 'evaluationDashboard.title']))

    const lit = rows.filter((a) => a.getAttribute('aria-current') === 'page')
    expect(lit.map((a) => a.textContent)).toEqual(['evaluationDashboard.title'])
  })
})

describe('the link check can fail', () => {
  it('finds no section link in the markup the trail used to render', () => {
    const { container } = render(<nav><a>home</a><span aria-current="page">rfq.title</span></nav>)
    const trail = container.querySelector('nav') as HTMLElement

    expect(sectionLink(trail, { to: '/back-office/rfqs', labelKey: 'rfq.title' })).toBeNull()
    expect(currentMarks(trail)).toHaveLength(1)
  })

  it('finds it once the section is a real link', () => {
    const { container } = render(<nav><a href="/back-office/dashboard">nav.dashboard</a><a href="/back-office/rfqs">rfq.title</a></nav>)
    const trail = container.querySelector('nav') as HTMLElement

    expect(sectionLink(trail, { to: '/back-office/rfqs', labelKey: 'rfq.title' })).not.toBeNull()
    expect(currentMarks(trail)).toHaveLength(0)
  })
})

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

    await userEvent.click(screen.getAllByRole('link', { name: 'review.title' })[0])
    expect(toggle).toHaveAttribute('aria-expanded', 'false')
  })

  it('points aria-controls at a panel that is really in the document while shut', () => {
    renderBar('/back-office/review')
    const panel = document.getElementById(screen.getByRole('button', { name: 'nav.primaryLabel' }).getAttribute('aria-controls') ?? '')

    expect(panel).not.toBeNull()
    expect(panel).toHaveAttribute('hidden')
  })
})
