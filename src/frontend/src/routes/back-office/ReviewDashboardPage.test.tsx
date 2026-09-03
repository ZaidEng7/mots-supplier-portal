import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import { renderPage, mockFetch } from '../../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, Link: 'a' }
})

const { ReviewDashboardPage } = await import('./ReviewDashboardPage')

function reviewDashboard(overrides: Record<string, unknown> = {}) {
  return {
    pending: 4, underReview: 2, infoRequested: 1, unassigned: 3, assignedToMe: 2,
    oldestOpenCaseAgeDays: 9,
    expiryWatchlist: [
      {
        supplierReferenceCode: 'SUP-1', supplierDisplayNameAr: 'مورد', supplierDisplayNameEn: 'Acme',
        documentTypeCode: 'commercial_registration', state: 'ExpiringSoon', expiryDate: '2026-10-01',
      },
    ],
    ...overrides,
  }
}

describe('ReviewDashboardPage (SCR-300)', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('renders FR-DSH-002\'s KPIs and the expiry watchlist', async () => {
    restore = mockFetch({ '/api/v1/review/dashboard': reviewDashboard() })

    renderPage(<ReviewDashboardPage />)

    expect(await screen.findByText('Pending')).toBeInTheDocument()
    expect(screen.getByText('Info requested')).toBeInTheDocument()
    expect(screen.getByText('Acme')).toBeInTheDocument()
  })

  it('reports aging as a duration and never as a breach', async () => {
    // No document defines a review SLA, so the screen must not imply a threshold. This is the
    // assertion that stops "9 days" quietly becoming "overdue" later.
    restore = mockFetch({ '/api/v1/review/dashboard': reviewDashboard() })

    renderPage(<ReviewDashboardPage />)

    expect(await screen.findByText('The oldest open case has been waiting 9 days.')).toBeInTheDocument()
    expect(screen.queryByText(/overdue/i)).not.toBeInTheDocument()
    expect(screen.queryByText(/breach/i)).not.toBeInTheDocument()
  })

  it('says so plainly when there is nothing open', async () => {
    // The control for the aging line: with no open cases there is no duration to report, and the
    // screen says that rather than showing a zero that reads as "instant".
    restore = mockFetch({
      '/api/v1/review/dashboard': reviewDashboard({ oldestOpenCaseAgeDays: null, expiryWatchlist: [] }),
    })

    renderPage(<ReviewDashboardPage />)

    expect(await screen.findByText('No open cases.')).toBeInTheDocument()
    expect(screen.getByText('No documents are nearing expiry')).toBeInTheDocument()
  })
})
