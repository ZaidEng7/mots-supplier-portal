import { describe, expect, it, vi } from 'vitest'
import type { ReactNode } from 'react'
import { render, screen, within } from '@testing-library/react'
import { LayoutDashboard, List } from 'lucide-react'
import { APPARENT_STROKE, Icon, iconWeight } from '../components/ui/Icon'
import type { NavGroup } from './navigation'

vi.mock('@tanstack/react-router', () => ({
  Link: ({ to, children, ...rest }: { to: string; children: ReactNode }) => <a href={to} {...rest}>{children}</a>,
}))

const { NavGroups, Sidebar, isCurrent, visibleGroups } = await import('./Sidebar')

const GROUPS: NavGroup[] = [
  {
    items: [{ to: '/back-office/dashboard', labelKey: 'nav.dashboard', icon: LayoutDashboard }],
  },
  {
    headingKey: 'nav.groupTenders',
    items: [
      { to: '/back-office/rfqs', labelKey: 'rfq.title', icon: List },
      { to: '/back-office/review', labelKey: 'review.title', icon: List, exact: true },
      { to: '/back-office/admin', labelKey: 'adminOverview.title', icon: List, when: (c) => c.can('admin.users.manage') },
    ],
  },
]

const EVERYTHING = { can: () => true, inABuyingBody: true }
const NOTHING = { can: () => false, inABuyingBody: false }

/**
 * The rail replaced a wrapping row of up to thirty-one identically-coloured links with no grouping and
 * no marker for the page you were on. These are the three things that row could not do.
 */
describe('the sidebar says where you can go and where you are', () => {
  it('marks exactly one row as the page, and marks the section a deeper path belongs to', () => {
    render(<NavGroups groups={GROUPS} context={EVERYTHING} pathname="/back-office/rfqs/RFQ-2026-000001/award" />)

    const current = screen.getAllByRole('link').filter((a) => a.getAttribute('aria-current') === 'page')
    expect(current).toHaveLength(1)
    expect(current[0]).toHaveTextContent('rfq.title')
  })

  /**
   * The denominator. `aria-current` on every row, or on none, satisfies a test that only asks whether
   * the current row can be found - and both are the flat row again, which marked nothing at all.
   */
  it('marks nothing when the path is a destination the rail does not hold', () => {
    render(<NavGroups groups={GROUPS} context={EVERYTHING} pathname="/back-office/nowhere" />)

    expect(screen.getAllByRole('link').filter((a) => a.hasAttribute('aria-current'))).toEqual([])
  })

  /** `/back-office/review` is a prefix of `/back-office/review/suppliers`, which is what `exact` is for. */
  it('does not light a row whose path merely prefixes another', () => {
    expect(isCurrent({ to: '/back-office/review', labelKey: 'x', icon: List, exact: true }, '/back-office/review/suppliers')).toBe(false)
    expect(isCurrent({ to: '/back-office/rfqs', labelKey: 'x', icon: List }, '/back-office/rfqs/RFQ-1')).toBe(true)
    // And a sibling that merely starts with the same characters is not a child.
    expect(isCurrent({ to: '/back-office/review', labelKey: 'x', icon: List }, '/back-office/review-dashboard')).toBe(false)
  })

  it('gives every group its own named landmark', () => {
    render(<NavGroups groups={GROUPS} context={EVERYTHING} pathname="/back-office/dashboard" />)

    // §D3 settled this: a screen-reader user jumps between the named groups the same way a sighted
    // reader jumps between the headings. One landmark holding every row is the flat list with a border.
    expect(screen.getByRole('navigation', { name: 'nav.groupTenders' })).toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: 'nav.groupOverview' })).toBeInTheDocument()
  })

  it('hides a row the account has no permission for, and drops a group left empty', () => {
    const gated: NavGroup[] = [{ headingKey: 'nav.groupTenders', items: [GROUPS[1].items[2]] }]

    expect(visibleGroups(gated, EVERYTHING)).toHaveLength(1)
    expect(visibleGroups(gated, NOTHING)).toEqual([])
  })

  it('renders no heading for a group that lost every row rather than a heading over nothing', () => {
    render(<NavGroups groups={[{ headingKey: 'nav.groupTenders', items: [GROUPS[1].items[2]] }]} context={NOTHING} pathname="/x" />)

    expect(screen.queryByText('nav.groupTenders')).toBeNull()
  })

  it('shows who is signed in, by the only name the token actually carries', () => {
    render(
      <Sidebar
        groups={GROUPS} context={EVERYTHING} pathname="/back-office/dashboard"
        title="Portal" subtitle="Back office" account={{ email: 'officer@mots.local' }}
      />,
    )

    expect(screen.getByText('officer@mots.local')).toBeInTheDocument()
  })

  it('says nothing about who is signed in when nobody is', () => {
    const { container } = render(
      <Sidebar groups={GROUPS} context={EVERYTHING} pathname="/back-office/dashboard" title="Portal" subtitle="Back office" />,
    )

    expect(within(container).queryByTitle(/@/)).toBeNull()
  })
})

/**
 * The glyphs are drawn on a 24-unit artboard and the templates they copy were drawn on a 16-unit one,
 * so a fixed stroke width would have thinned every icon as it grew. This is that conversion.
 */
describe('icons keep one weight at every size', () => {
  it('solves the stroke so the line lands at the same width whatever the size', () => {
    for (const size of [15, 18, 24, 32]) {
      expect(iconWeight(size) * (size / 24)).toBeCloseTo(APPARENT_STROKE, 6)
    }
  })

  it('the conversion can fail', () => {
    // Revert-to-red: a fixed stroke width is exactly what this replaces, and it does NOT hold.
    const fixed = 2
    expect(fixed * (15 / 24)).not.toBeCloseTo(fixed * (32 / 24), 2)
  })

  it('hides the glyph from a screen reader, because every one of them sits beside its own word', () => {
    const { container } = render(<Icon as={List} />)

    expect(container.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true')
  })
})
