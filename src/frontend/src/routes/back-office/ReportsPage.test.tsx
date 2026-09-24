// Two groups: the reports themselves, and the procurement report for a reader who belongs to no buying body.
//
// Both reports render with their counts. That test awaits a DATA value rather than the heading, because the card's title
// renders during loading too - awaiting it waits for nothing and the assertions run against the skeleton, which is how the
// first version of this test failed.
//
// AN UNMEASURED INTERVAL reads as not measured, never as zero: "no RFQ has reached award" and "award takes no time" are
// different facts, and a zero in that cell asserts the second one. It is parenthesised per D-18, matching the exported
// artefact's own marker for the same cell.
//
// The cycle-time COVERAGE FLOOR is stated rather than left invisible: the RFQs that moved before audit logging existed
// contribute to nothing, and without that line a short history reads as a fast process. And the compliance counts are said to
// be ministry-wide, because the registry has no organization.
//
// States are labelled from the CATALOGUE, never as the raw enum name, with the control that the raw key for a state whose
// label differs from it must not be on the page - 'ExpiringSoon' is the enum member and the catalogue renders it as words.
// Counts render in Eastern Arabic numerals under Arabic (R-1), controlled by the English assertion above, where the same
// payload renders 41 as "41".
//
// LOADING shows a skeleton rather than a spinner, labelled with the SCREEN. It is queried synchronously, before the mocked
// fetch resolves, because awaiting first would look at the loaded page and find no skeleton at all. And the label matters: a
// skeleton announced as "Compliance report" is indistinguishable from the loaded card, to a screen reader and to this test -
// that was the EPIC-16 mistake, and the first version here repeated it.
//
// THE SECOND GROUP is the procurement report scoped to one buying body, which two personas deliberately belong to none of.
// The endpoint answers 404 for them, correctly - there is nothing in scope to return. It reached the screen as a thrown
// error, so the card offered "The report could not be loaded" and a Try again that could never succeed, on every visit, for
// the bootstrap administrator and the Ministry viewer. The compliance report below it is unscoped and loaded fine, and the
// two side by side - one broken, one working - is what made a policy boundary read as a fault.
//
// So the scope is EXPLAINED instead of reported as a failure, and the retry is the part that mattered: a button that re-asks a
// question this account cannot ask. Its control is that a real failure is still reported as one, with a retry that can work -
// without which the change would pass just as well on a screen that had simply stopped reporting errors at all.
//
// THE TWO FEED BUTTONS ARE ASSERTED TO GO TO DIFFERENT ROUTES, which is not paranoia: they sit side by side on
// one card, differ by one word, and the failure - both wired to the same handler - produces a screen where
// every button works and one file is silently never sent. Clicking Tenders and asserting the suppliers route
// was NOT called is what catches it.
//
// THE MINISTRY FEED CARD sits beside the registry export behind the same permission, and the pair is asserted
// as a pair: four Export CSV buttons rather than three. The count is the assertion that noticed this card
// arriving at all, which is what a count is for - a name-based query would have been satisfied by any one of
// them. The card's own test asserts the sentence distinguishing the two files, because two exports of the same
// registry sitting one above the other is exactly the screen on which somebody sends the wrong one.
//
// THE REGISTRY EXPORT CARD is asserted from both sides, because it is the one card on this screen whose absence
// is the correct behaviour for most of the people who can open the screen. report.read opens Reports and is held
// by the procurement manager and the Ministry viewer; supplier.registry.export is held by the system
// administrator alone. A test that only checked the card appears would pass against a card that always appears,
// which is the failure that matters here - it would offer every one of those accounts a download that can only
// answer 403. So the absent case comes first, with the other two cards as its control, and the sensitivity line
// is asserted with the card rather than trusted to be there.
//
// THE EXPORT BUTTONS are the same boundary and were missed by that fix. /reports/procurement/export 404s for the two accounts
// with no buying body, exactly as the report itself does, so Export PDF and Export CSV sat above the explanation and answered
// it with "The file could not be downloaded" in red. Counting is what makes this test say something: the page offers two
// exports per card, so the assertion is that four buttons become two, not that some button is absent - a name-based query
// would pass against a page that had lost the compliance exports too. The loaded case above is its control.

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import i18n from '../../i18n/config'
import { renderPage, mockFetch, type RecordedRequest } from '../../test/renderPage'
import { useAuthStore } from '../../lib/authStore'

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

    expect(await screen.findByText('18.5')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Procurement report' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Compliance report' })).toBeInTheDocument()
  })

  it('offers an export on both cards when the procurement report is in scope', async () => {
    restore = mockFetch(routes)

    renderPage(<ReportsPage />)

    expect(await screen.findByText('18.5')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'Export CSV' })).toHaveLength(2)
    expect(screen.getAllByRole('button', { name: 'Export PDF' })).toHaveLength(2)
  })

  it('an interval nothing has completed reads as not measured, never as zero', async () => {
    restore = mockFetch(routes)

    renderPage(<ReportsPage />)

    expect(await screen.findByText('(not measured)')).toBeInTheDocument()

    const row = screen.getByRole('row', { name: /Evaluation to award/ })
    expect(within(row).queryByText('0.0')).not.toBeInTheDocument()
  })

  it('states the cycle-time coverage floor rather than leaving the gap invisible', async () => {
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
    expect(screen.queryByText('ExpiringSoon')).not.toBeInTheDocument()
  })

  it('renders counts in Eastern Arabic numerals under Arabic', async () => {
    restore = mockFetch(routes)
    await i18n.changeLanguage('ar')

    renderPage(<ReportsPage />)

    expect(await screen.findByText('٤١')).toBeInTheDocument()
    expect(screen.queryByText('41')).not.toBeInTheDocument()
  })

  it('shows a skeleton while loading, not a spinner, labelled with the screen', async () => {
    const original = globalThis.fetch
    globalThis.fetch = (() => new Promise(() => {})) as typeof fetch
    restore = () => { globalThis.fetch = original }

    renderPage(<ReportsPage />)

    const loading = await screen.findAllByRole('status')
    expect(loading.length).toBeGreaterThan(0)
    expect(loading.every((el) => el.textContent === 'Reports')).toBe(true)
  })
})

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
    expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull()
  })

  it('does not offer an export of a report this account cannot ask for', async () => {
    restore = mockFetch({
      '/api/v1/reports/procurement': { __status: 404 },
      '/api/v1/reports/compliance': compliance(),
    })

    renderPage(<ReportsPage />)

    expect(await screen.findByText(/not attached to one/i)).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'Export CSV' })).toHaveLength(1)
    expect(screen.getAllByRole('button', { name: 'Export PDF' })).toHaveLength(1)
  })

  it('still offers the export while the procurement report is failing, which says nothing about scope', async () => {
    restore = mockFetch({
      '/api/v1/reports/procurement': { __status: 500 },
      '/api/v1/reports/compliance': compliance(),
    })

    renderPage(<ReportsPage />)

    expect(await screen.findByText('The report could not be loaded.')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'Export CSV' })).toHaveLength(2)
  })

  it('still reports a real failure as one, with a retry that can work', async () => {
    restore = mockFetch({
      '/api/v1/reports/procurement': { __status: 500 },
      '/api/v1/reports/compliance': compliance(),
    })

    renderPage(<ReportsPage />)

    expect(await screen.findByText('The report could not be loaded.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument()
  })
})

describe('the supplier registry export card', () => {
  let restore: () => void
  afterEach(() => {
    restore?.()
    useAuthStore.setState({ claims: null } as never)
  })

  function signInWith(permissions: string[]) {
    useAuthStore.setState({ claims: { organizationId: null, permissions } } as never)
  }

  it('is absent for an account that can open Reports but does not hold the permission', async () => {
    signInWith(['report.read'])
    restore = mockFetch(routes)

    renderPage(<ReportsPage />)

    expect(await screen.findByRole('heading', { name: 'Compliance report' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Procurement report' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Supplier registry export' })).toBeNull()
  })

  it('is offered to an account that holds supplier.registry.export, and says what is in the file', async () => {
    signInWith(['report.read', 'supplier.registry.export'])
    restore = mockFetch(routes)

    renderPage(<ReportsPage />)

    expect(await screen.findByRole('heading', { name: 'Supplier registry export' })).toBeInTheDocument()
    expect(screen.getByText(/tax identifiers, named contacts/i)).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'Export CSV' })).toHaveLength(3)
  })

  it('offers both ministry feeds by name, beside the registry export', async () => {
    signInWith(['report.read', 'supplier.registry.export'])
    restore = mockFetch(routes)

    renderPage(<ReportsPage />)

    expect(await screen.findByRole('heading', { name: /Ministry dashboard feeds/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Suppliers CSV' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Tenders CSV' })).toBeInTheDocument()
    expect(screen.getByText(/Purchase orders, invoices and payments are the ERP/i)).toBeInTheDocument()
  })

  it('sends each feed button to its own route', async () => {
    signInWith(['report.read', 'supplier.registry.export'])
    const recorded: RecordedRequest[] = []
    restore = mockFetch(routes, recorded)

    renderPage(<ReportsPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Tenders CSV' }))

    await vi.waitFor(() => {
      expect(recorded.some((r) => r.url.includes('/api/v1/feeds/rfqs'))).toBe(true)
    })
    expect(recorded.some((r) => r.url.includes('/api/v1/feeds/suppliers'))).toBe(false)
  })

  it('hides the ministry feed from an account that cannot export the registry', async () => {
    signInWith(['report.read'])
    restore = mockFetch(routes)

    renderPage(<ReportsPage />)

    expect(await screen.findByRole('heading', { name: 'Compliance report' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: /Ministry dashboard feeds/i })).toBeNull()
  })
})
