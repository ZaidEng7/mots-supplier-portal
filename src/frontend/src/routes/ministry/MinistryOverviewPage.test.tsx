import { describe, expect, it, vi, afterEach } from 'vitest'
import { screen } from '@testing-library/react'
import { mockFetch, renderPage } from '../../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<typeof import('@tanstack/react-router')>('@tanstack/react-router')
  return { ...actual, Link: 'a' }
})

const { MinistryOverviewPage } = await import('./MinistryOverviewPage')

function overview(overrides: Record<string, unknown> = {}) {
  return {
    totalSuppliers: 12,
    suppliersByLifecycleState: [{ key: 'Active', count: 9 }, { key: 'Suspended', count: 3 }],
    totalRfqs: 7,
    rfqsByState: [{ key: 'SubmissionOpen', count: 4 }, { key: 'Awarded', count: 3 }],
    totalAwards: 3,
    averageProposalsPerRfq: 2.5,
    totalAwardedValue: null,
    commercialValuesVisible: false,
    ...overrides,
  }
}

/** SCR-600. The persona held an EMPTY permission set before EPIC-18, so "it renders at all" is the
 * first thing worth asserting. */
describe('MinistryOverviewPage', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('renders the aggregate KPIs', async () => {
    restore = mockFetch({ '/api/v1/ministry/overview': overview() })

    renderPage(<MinistryOverviewPage />)

    expect(await screen.findByText('Governance dashboard')).toBeInTheDocument()
    expect(screen.getByText('Registered suppliers')).toBeInTheDocument()
    expect(screen.getByText('12')).toBeInTheDocument()
    // Catalogue labels, not raw enum names.
    expect(screen.getByText('Open for submissions')).toBeInTheDocument()
  })

  it('says WHY the commercial figure is absent rather than rendering a blank', async () => {
    // D-6/BRULE-087: the flag is off by default. A viewer seeing an empty tile cannot tell policy from
    // an empty ministry, which is the whole reason the response echoes the flag.
    restore = mockFetch({ '/api/v1/ministry/overview': overview() })

    renderPage(<MinistryOverviewPage />)

    expect(await screen.findByText('Commercial values are not shown')).toBeInTheDocument()
    expect(screen.getByText(/aggregate metrics are shown without commercial values/)).toBeInTheDocument()
  })

  it('shows the commercial figure once the policy flag is on', async () => {
    // The control for the test above: the same screen, the same shape, the flag flipped.
    restore = mockFetch({
      '/api/v1/ministry/overview': overview({ totalAwardedValue: 1250.5, commercialValuesVisible: true }),
    })

    renderPage(<MinistryOverviewPage />)

    expect(await screen.findByText('1,250.50')).toBeInTheDocument()
    expect(screen.queryByText('Commercial values are not shown')).not.toBeInTheDocument()
  })

  it('offers a retry instead of a blank page when the read fails', async () => {
    restore = mockFetch({ '/api/v1/ministry/overview': { __status: 500 } })

    renderPage(<MinistryOverviewPage />)

    expect(await screen.findByText('Could not load the governance dashboard')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument()
  })
})
