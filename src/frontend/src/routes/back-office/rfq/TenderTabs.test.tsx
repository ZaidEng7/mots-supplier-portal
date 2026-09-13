// The buyer's six views of one tender.
//
// What this replaces is a page that stacked every section down one column and reached the bids, the comparison and the award
// through buttons two thirds of the way down it. The strip has to be right about exactly two things: where each tab goes,
// and which one you are on.
//
// These use renderPage rather than a bare render, because it is what initialises i18n and these assertions are about the
// words a reader sees on the tabs. The fixture answers the two reads the strip makes for itself - seven invited and four
// bids.
//
// Each tab goes to its own route, carrying the reference code, and exactly one is marked as the page you are on. The
// denominator for that is the reason the tender tab is matched exactly rather than by prefix: every other tab's path begins
// with the tender's own, so a prefix match would light up "Tender" from inside Settings - the same defect the supplier
// navigation had against its dashboard link, where every page in the product claimed to be the dashboard.
//
// THE COUNTS are why these are tabs rather than a menu: "Suppliers" and "Suppliers 7" ask a reader for different amounts of
// work, and the second answers a question they would otherwise open the tab to ask. Only the two tabs that have a number
// get one - a count of nothing on Award would be an invitation to wonder what it counted. That test waits for one of the
// strip's own two queries, and reads the labels as TEXT rather than by accessible name, because the name is what it asserts:
// a count glued to its label is what a screen reader would say without the separator between them.
//
// The denominator for the counts is the last test. A strip that printed 0 when the request failed would state a fact - that
// nobody has bid - because it could not read one. No number is the honest answer to not knowing, and the strip is navigation
// first: every destination is still there, which is the half that must not break.

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { screen, within } from '@testing-library/react'
import { renderPage, mockFetch } from '../../../test/renderPage'

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
  return renderPage(<TenderTabs referenceCode="RFQ-2026-000001" />)
}

describe('TenderTabs', () => {
  let restore: (() => void) | undefined

  beforeEach(() => {
    restore = mockFetch({
      '/api/v1/rfqs/RFQ-2026-000001/workspace': {
        rfqReferenceCode: 'RFQ-2026-000001', rfqState: 'SubmissionOpen', isCancelled: false,
        submittedProposalCount: 4, evaluationState: null, awardState: null, stages: [], nextActions: [],
      },
      '/api/v1/rfqs/RFQ-2026-000001': {
        referenceCode: 'RFQ-2026-000001', titleAr: 'ط', titleEn: 'T', state: 'SubmissionOpen',
        items: [], requirements: [], attachments: [], approvals: [], clarifications: [], addenda: [],
        invitations: Array.from({ length: 7 }, (_, i) => ({ id: `i-${i}`, supplierId: `s-${i}` })),
      },
    })
  })

  afterEach(() => {
    restore?.(); restore = undefined
    pathname = '/back-office/rfqs/RFQ-2026-000001'
  })

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

  it('does not mark the tender tab as current from inside another tab', () => {
    pathname = '/back-office/rfqs/RFQ-2026-000001/settings'
    renderTabs()

    const tender = screen.getByRole('link', { name: 'Tender' })
    expect(tender).not.toHaveAttribute('aria-current')

    const current = screen.getAllByRole('link').filter((a) => a.getAttribute('aria-current') === 'page')
    expect(current).toHaveLength(1)
    expect(current[0]).toHaveTextContent('Settings')
  })

  it('carries a count on the two tabs that have one, and on no others', async () => {
    renderTabs()

    await screen.findByText('7')

    const labelled = Object.fromEntries(
      screen.getAllByRole('link').map((a) => [a.getAttribute('href')?.split('/').pop(), a.textContent]),
    )

    expect(labelled['suppliers']).toBe('Suppliers 7')
    expect(labelled['proposals']).toBe('Bids 4')
    expect(labelled['award']).toBe('Award')
    expect(labelled['settings']).toBe('Settings')
  })

  it('shows no count rather than a zero when the count cannot be read', async () => {
    restore?.()
    restore = mockFetch({})

    renderTabs()

    const nav = await screen.findByRole('navigation', { name: 'Tender sections' })
    const labelled = Object.fromEntries(
      within(nav).getAllByRole('link').map((a) => [a.getAttribute('href')?.split('/').pop(), a.textContent]),
    )

    expect(labelled['suppliers']).toBe('Suppliers')
    expect(labelled['proposals']).toBe('Bids')
    expect(within(nav).getAllByRole('link')).toHaveLength(6)
  })
})
