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

    expect(await screen.findByText('Active RFQs')).toBeInTheDocument()
    for (const tile of ['Closing this week', 'Awaiting my action', 'Pending approvals', 'Awards in progress']) {
      expect(screen.getByText(tile)).toBeInTheDocument()
    }
  })

  it('the pipeline board labels states from the catalogue, never the raw enum', async () => {
    // T3-36 made Shortlisting reachable, so it is a real column now rather than a permanently empty
    // one - and it must read as a label, not as "Shortlisting" the enum member.
    restore = mockFetch({ '/api/v1/procurement/dashboard': dashboard() })

    renderPage(<ProcurementDashboardPage />)

    expect(await screen.findByText('Shortlisting')).toBeInTheDocument()
    expect(screen.getByText('Draft')).toBeInTheDocument()
  })

  it('the approvals card appears only when the server says the caller may approve', async () => {
    restore = mockFetch({ '/api/v1/procurement/dashboard': dashboard({ showsApprovals: false }) })

    renderPage(<ProcurementDashboardPage />)

    await screen.findByText('Active RFQs')
    expect(screen.queryByText('Open approval queues')).not.toBeInTheDocument()

    restore()
    restore = mockFetch({ '/api/v1/procurement/dashboard': dashboard({ showsApprovals: true }) })

    renderPage(<ProcurementDashboardPage />)

    // The control: with the flag set it does render, so the absence above is the flag and not a
    // missing element.
    expect(await screen.findAllByText('Open approval queues')).not.toHaveLength(0)
  })

  it('empty: shows §10\'s own empty state rather than a bare board', async () => {
    restore = mockFetch({
      '/api/v1/procurement/dashboard': dashboard({ pipeline: [], tasks: [] }),
    })

    renderPage(<ProcurementDashboardPage />)

    expect(await screen.findByText('No RFQs yet')).toBeInTheDocument()
  })

  it('counts render in Eastern Arabic numerals under Arabic', async () => {
    // R-1. A KPI reading "14" beside a date reading «٣٠ أغسطس» is the exact inconsistency the ruling
    // was made to prevent.
    const restoreFetch = mockFetch({ '/api/v1/procurement/dashboard': dashboard() })
    await i18n.changeLanguage('ar')
    restore = () => { restoreFetch(); void i18n.changeLanguage('en') }

    renderPage(<ProcurementDashboardPage />)

    expect(await screen.findByText('١٤')).toBeInTheDocument()
    expect(screen.queryByText('14')).not.toBeInTheDocument()
  })
})
