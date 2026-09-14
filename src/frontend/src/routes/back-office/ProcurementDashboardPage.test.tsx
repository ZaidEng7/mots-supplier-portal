// SCR-400. §10's five KPI tiles render.
//
// The pipeline board labels states from the CATALOGUE, never the raw enum: T3-36 made Shortlisting reachable, so it is a real
// column now rather than a permanently empty one - and it must read as a label rather than as the enum member "Shortlisting".
//
// The approvals card appears only when the SERVER says the caller may approve, with the control that it does render when the
// flag is set - so the absence is the flag rather than a missing element.
//
// Empty shows §10's own empty state rather than a bare board.
//
// And counts render in Eastern Arabic numerals under Arabic (R-1): a KPI reading "14" beside a date reading «٣٠ أغسطس» is the
// exact inconsistency the ruling was made to prevent.

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import i18n from '../../i18n/config'
import { renderPage, mockFetch } from '../../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, Link: 'a' }
})

const { ProcurementDashboardPage } = await import('./ProcurementDashboardPage')

function dashboard(overrides: Record<string, unknown> = {}) {
  return {
    kpis: {
      activeRfqs: 14, closingThisWeek: 2, awaitingMyAction: 3,
      pendingApprovals: 1, awardsInProgress: 4,
    },
    pipeline: [
      { state: 'Draft', count: 6, nearestDeadline: null },
      { state: 'Shortlisting', count: 2, nearestDeadline: '2026-09-30T12:00:00Z' },
    ],
    tasks: [
      { rfqReferenceCode: 'RFQ-2026-000001', titleAr: 'طلب', titleEn: 'Catering', kind: 'SubmissionClosing', due: '2026-09-20T12:00:00Z' },
    ],
    showsApprovals: false,
    ...overrides,
  }
}

describe('ProcurementDashboardPage (SCR-400)', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('renders §10\'s five KPI tiles', async () => {
    restore = mockFetch({ '/api/v1/procurement/dashboard': dashboard() })

    renderPage(<ProcurementDashboardPage />)

    expect(await screen.findByText('Active tenders')).toBeInTheDocument()
    for (const tile of ['Closing this week', 'Awaiting my action', 'Pending approvals', 'Awards in progress']) {
      expect(screen.getByText(tile)).toBeInTheDocument()
    }
  })

  it('the pipeline board labels states from the catalogue, never the raw enum', async () => {
    restore = mockFetch({ '/api/v1/procurement/dashboard': dashboard() })

    renderPage(<ProcurementDashboardPage />)

    expect(await screen.findByText('Shortlisting')).toBeInTheDocument()
    expect(screen.getByText('Draft')).toBeInTheDocument()
  })

  it('the approvals card appears only when the server says the caller may approve', async () => {
    restore = mockFetch({ '/api/v1/procurement/dashboard': dashboard({ showsApprovals: false }) })

    renderPage(<ProcurementDashboardPage />)

    await screen.findByText('Active tenders')
    expect(screen.queryByText('Open approval queues')).not.toBeInTheDocument()

    restore()
    restore = mockFetch({ '/api/v1/procurement/dashboard': dashboard({ showsApprovals: true }) })

    renderPage(<ProcurementDashboardPage />)

    expect(await screen.findAllByText('Open approval queues')).not.toHaveLength(0)
  })

  it('empty: shows §10\'s own empty state rather than a bare board', async () => {
    restore = mockFetch({
      '/api/v1/procurement/dashboard': dashboard({ pipeline: [], tasks: [] }),
    })

    renderPage(<ProcurementDashboardPage />)

    expect(await screen.findByText('No tenders yet')).toBeInTheDocument()
  })

  it('counts render in Eastern Arabic numerals under Arabic', async () => {
    const restoreFetch = mockFetch({ '/api/v1/procurement/dashboard': dashboard() })
    await i18n.changeLanguage('ar')
    restore = () => { restoreFetch(); void i18n.changeLanguage('en') }

    renderPage(<ProcurementDashboardPage />)

    expect(await screen.findByText('١٤')).toBeInTheDocument()
    expect(screen.queryByText('14')).not.toBeInTheDocument()
  })
})
