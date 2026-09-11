import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, within } from '@testing-library/react'
import i18n from '../../i18n/config'
import { renderPage, mockFetch } from '../../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, Link: 'a' }
})

const { ReportsPage } = await import('./ReportsPage')

function procurement(overrides: Record<string, unknown> = {}) {
  return {
    rfqsByState: [
      { key: 'Draft', count: 6 },
      { key: 'Published', count: 12 },
    ],
    cycleTimes: [
      { key: 'ReviewToApproved', sampleSize: 24, medianHours: 18.5 },
      { key: 'EvaluationToAward', sampleSize: 0, medianHours: null },
    ],
    awardsByState: [{ key: 'Recommended', count: 3 }],
    totalRfqs: 18,
    coverageFloor: '2026-06-05T09:00:00Z',
    ...overrides,
  }
}

function compliance(overrides: Record<string, unknown> = {}) {
  return {
    suppliersByLifecycleState: [{ key: 'Active', count: 41 }],
    documentsByState: [{ key: 'ExpiringSoon', count: 7 }],
    totalSuppliers: 41,
    documentsExpiringSoon: 7,
    documentsExpired: 2,
    ...overrides,
  }
}

const routes = {
  '/api/v1/reports/procurement': procurement(),
  '/api/v1/reports/compliance': compliance(),
}

describe('ReportsPage (/back-office/reports — screen design is an invention)', () => {
  let restore: () => void
  afterEach(async () => {
    restore?.()
    await i18n.changeLanguage('en')
  })

  it('renders both reports with their counts', async () => {
    restore = mockFetch(routes)

    renderPage(<ReportsPage />)

    // Awaited on a DATA value, not on the heading. The card's title renders during loading too, so
    // awaiting it waits for nothing and the assertions below run against the skeleton - which is
    // how the first version of this test failed.
    expect(await screen.findByText('18.5')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Procurement report' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Compliance report' })).toBeInTheDocument()
  })

  it('an interval nothing has completed reads as not measured, never as zero', async () => {
    // "No RFQ has reached award" and "award takes no time" are different facts, and a zero in this
    // cell asserts the second one.
    restore = mockFetch(routes)

    renderPage(<ReportsPage />)

    // D-18: parenthesised, matching the exported artefact's own marker for the same cell.
    expect(await screen.findByText('(not measured)')).toBeInTheDocument()

    const row = screen.getByRole('row', { name: /Evaluation to award/ })
    expect(within(row).queryByText('0.0')).not.toBeInTheDocument()
  })

  it('states the cycle-time coverage floor rather than leaving the gap invisible', async () => {
    // The RFQs that moved before audit logging existed contribute to nothing. Without this line a
    // short history reads as a fast process.
    restore = mockFetch(routes)

    renderPage(<ReportsPage />)

    expect(await screen.findByText(/Cycle times are measured from/)).toBeInTheDocument()
  })

  it('says the compliance counts are ministry-wide, because the registry has no organization', async () => {
    restore = mockFetch(routes)

    renderPage(<ReportsPage />)

    expect(
      await screen.findByText(/cover every registered supplier, not only your organization/),
    ).toBeInTheDocument()
  })

  it('labels states from the catalogue, never as the raw enum name', async () => {
    restore = mockFetch(routes)

    renderPage(<ReportsPage />)

    expect(await screen.findByText('Published')).toBeInTheDocument()
    // The control: the raw key for a state whose label DIFFERS from it must not be on the page.
    // 'ExpiringSoon' is the enum member; the catalogue renders it as words.
    expect(screen.queryByText('ExpiringSoon')).not.toBeInTheDocument()
  })

  it('renders counts in Eastern Arabic numerals under Arabic', async () => {
    // R-1. The control is the English assertion above: the same payload renders 41 as "41" there.
    restore = mockFetch(routes)
    await i18n.changeLanguage('ar')

    renderPage(<ReportsPage />)

    expect(await screen.findByText('٤١')).toBeInTheDocument()
    expect(screen.queryByText('41')).not.toBeInTheDocument()
  })

  it('shows a skeleton while loading, not a spinner, labelled with the screen', async () => {
    restore = mockFetch(routes)

    renderPage(<ReportsPage />)

    // Queried synchronously, before the mocked fetch resolves - awaiting first would look at the
    // loaded page and find no skeleton at all.
    //
    // Labelled with the SCREEN rather than the card: a skeleton announced as "Compliance report" is
    // indistinguishable from the loaded card, to a screen reader and to this test. That was the
    // EPIC-16 mistake and the first version here repeated it.
    const loading = screen.getAllByRole('status')
    expect(loading.length).toBeGreaterThan(0)
    expect(loading.every((el) => el.textContent === 'Reports')).toBe(true)
  })
})

/**
 * The procurement report is scoped to one buying body; two personas deliberately belong to none.
 *
 * <p>The endpoint answers 404 for them, correctly - there is nothing in scope to return. It reached the
 * screen as a thrown error, so the card offered "The report could not be loaded" and a Try again that
 * could never succeed, on every visit, for the bootstrap administrator and the Ministry viewer. The
 * compliance report below it is unscoped and loaded fine, and the two side by side - one broken, one
 * working - is what made a policy boundary read as a fault.</p>
 */
describe('the procurement report when the reader belongs to no buying body', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('explains the scope instead of reporting a failure', async () => {
    restore = mockFetch({
      '/api/v1/reports/procurement': { __status: 404 },
      '/api/v1/reports/compliance': compliance(),
    })

    renderPage(<ReportsPage />)

    expect(await screen.findByText(/not attached to one/i)).toBeInTheDocument()
    expect(screen.queryByText('The report could not be loaded.')).toBeNull()
    // The retry is the part that mattered: a button that re-asks a question this account cannot ask.
    expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull()
  })

  it('still reports a real failure as one, with a retry that can work', async () => {
    // The control. Without this the change above would pass just as well on a screen that had simply
    // stopped reporting errors at all.
    restore = mockFetch({
      '/api/v1/reports/procurement': { __status: 500 },
      '/api/v1/reports/compliance': compliance(),
    })

    renderPage(<ReportsPage />)

    expect(await screen.findByText('The report could not be loaded.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument()
  })
})
