import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, within } from '@testing-library/react'
import { renderPage } from '../../../test/renderPage'

let pathname = '/back-office/rfqs/RFQ-2026-000001'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return {
    ...actual,
    useRouterState: () => pathname,
    Link: ({ to, params, children, ...rest }: { to: string; params?: Record<string, string>; children: React.ReactNode }) => {
      const href = Object.entries(params ?? {}).reduce((path, [key, value]) => path.replace(`$${key}`, value), to)
      return <a href={href} {...rest}>{children}</a>
    },
  }
})

const { TenderTabs } = await import('./TenderTabs')

function renderTabs() {
  // `renderPage` rather than a bare render: it is what initialises i18n, and these assertions are
  // about the words a reader sees on the tabs.
  return renderPage(<TenderTabs referenceCode="RFQ-2026-000001" invitedCount={7} bidCount={4} />)
}

/**
 * The buyer's six views of one tender.
 *
 * <p>What this replaces is a page that stacked every section down one column and reached the bids, the
 * comparison and the award through buttons two thirds of the way down it. The strip has to be right
 * about exactly two things: where each tab goes, and which one you are on.</p>
 */
describe('TenderTabs', () => {
  afterEach(() => { pathname = '/back-office/rfqs/RFQ-2026-000001' })

  it('sends each tab to its own route, carrying the reference code', () => {
    renderTabs()
    const nav = screen.getByRole('navigation', { name: 'Tender sections' })

    const hrefs = within(nav).getAllByRole('link').map((a) => a.getAttribute('href'))
    expect(hrefs).toEqual([
      '/back-office/rfqs/RFQ-2026-000001',
      '/back-office/rfqs/RFQ-2026-000001/suppliers',
      '/back-office/rfqs/RFQ-2026-000001/proposals',
      '/back-office/rfqs/RFQ-2026-000001/comparison',
      '/back-office/rfqs/RFQ-2026-000001/award',
      '/back-office/rfqs/RFQ-2026-000001/settings',
    ])
  })

  it('marks exactly one tab as the page you are on', () => {
    pathname = '/back-office/rfqs/RFQ-2026-000001/proposals'
    renderTabs()

    const current = screen.getAllByRole('link').filter((a) => a.getAttribute('aria-current') === 'page')
    expect(current).toHaveLength(1)
    expect(current[0]).toHaveTextContent('Bids')
  })

  /**
   * The denominator, and the reason the tender tab is matched exactly rather than by prefix. Every
   * other tab's path begins with the tender's own, so a prefix match would light up "Tender" from
   * inside Settings - which is the same defect the supplier navigation had against its dashboard link,
   * where every page in the product claimed to be the dashboard.
   */
  it('does not mark the tender tab as current from inside another tab', () => {
    pathname = '/back-office/rfqs/RFQ-2026-000001/settings'
    renderTabs()

    const tender = screen.getByRole('link', { name: 'Tender' })
    expect(tender).not.toHaveAttribute('aria-current')

    const current = screen.getAllByRole('link').filter((a) => a.getAttribute('aria-current') === 'page')
    expect(current).toHaveLength(1)
    expect(current[0]).toHaveTextContent('Settings')
  })

  /**
   * The counts are why these are here rather than in a menu: "Suppliers" and "Suppliers 7" ask a reader
   * for different amounts of work, and the second answers a question they would otherwise open the tab
   * to ask. Only the two tabs that have a number get one - a count of nothing on Award would be an
   * invitation to wonder what it counted.
   */
  it('carries a count on the two tabs that have one, and on no others', () => {
    renderTabs()

    // Read as text rather than by accessible name, because the name is what this asserts: a count glued
    // to its label is what a screen reader would say without the separator between them.
    const labelled = Object.fromEntries(
      screen.getAllByRole('link').map((a) => [a.getAttribute('href')?.split('/').pop(), a.textContent]),
    )

    expect(labelled['suppliers']).toBe('Suppliers 7')
    expect(labelled['proposals']).toBe('Bids 4')
    expect(labelled['award']).toBe('Award')
    expect(labelled['settings']).toBe('Settings')
  })
})
