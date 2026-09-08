import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { mockFetch, renderPage, type RecordedRequest } from '../test/renderPage'

// The harness deliberately has no router (see renderPage's own note), and this page links each row to
// the application. Same treatment as every other test of a page that links: Link becomes a plain anchor,
// so the route it points at is still assertable without pulling the whole route tree into the test.
vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, Link: 'a' }
})

const { ComplianceDirectoryPage } = await import('./ComplianceDirectoryPage')

/**
 * SCR-307. The list that did not exist: the review queue holds undecided applications only, so an approved
 * supplier whose certificate expired afterwards appeared nowhere, and the reviewer's only route to them was
 * to type the reference code into the address bar.
 */

const COMPLIANCE = '/api/v1/review/suppliers'

function envelope(data: unknown[], nextCursor: string | null = null) {
  return {
    data,
    pagination: { mode: 'cursor', nextCursor, prevCursor: null, pageSize: 20, totalCount: null, hasMore: nextCursor !== null },
    meta: { sort: 'displayNameEn', filtersApplied: null },
  }
}

const EXPIRED = {
  supplierCode: 'SUP-2026-000001', displayNameAr: 'شركة منتهية', displayNameEn: 'Lapsed Co',
  onboardingState: 'Approved', lifecycleState: 'Suspended', createdAt: '2026-01-15T09:00:00Z',
  expiredDocumentCount: 1, expiringDocumentCount: 2, rejectedDocumentCount: 0,
}

const HEALTHY = {
  supplierCode: 'SUP-2026-000002', displayNameAr: 'شركة سليمة', displayNameEn: 'Healthy Co',
  onboardingState: 'Approved', lifecycleState: 'Active', createdAt: '2026-02-20T09:00:00Z',
  expiredDocumentCount: 0, expiringDocumentCount: 0, rejectedDocumentCount: 0,
}

let restore: (() => void) | undefined
afterEach(() => restore?.())

describe('ComplianceDirectoryPage', () => {
  it('counts expired and expiring documents separately rather than summing them', async () => {
    // The distinction the screen exists to preserve: expiring is a prompt, expired is a bar, and one
    // combined number would not tell a reviewer whether this supplier can currently hold a contract.
    restore = mockFetch({ [COMPLIANCE]: envelope([EXPIRED, HEALTHY]) })

    renderPage(<ComplianceDirectoryPage />)

    expect(await screen.findByText('Lapsed Co')).toBeInTheDocument()
    expect(screen.getByText('Expired: 1')).toBeInTheDocument()
    expect(screen.getByText('Expiring: 2')).toBeInTheDocument()
    expect(screen.queryByText('Expired: 3')).not.toBeInTheDocument()
  })

  it('says a supplier with nothing outstanding is healthy, rather than showing three zeroes', async () => {
    restore = mockFetch({ [COMPLIANCE]: envelope([HEALTHY]) })

    renderPage(<ComplianceDirectoryPage />)

    expect(await screen.findByText('Healthy Co')).toBeInTheDocument()
    expect(screen.getByText('Healthy')).toBeInTheDocument()
  })

  it('links each row to the application, which was the missing route', async () => {
    // F-6's other half. A decided case dropped out of every list, so the reviewer who wanted to look at
    // their own decision had the address bar and nothing else.
    restore = mockFetch({ [COMPLIANCE]: envelope([EXPIRED]) })

    renderPage(<ComplianceDirectoryPage />)

    const link = (await screen.findByText('Lapsed Co')).closest('a')
    expect(link).not.toBeNull()
    expect(link!.getAttribute('to')).toBe('/back-office/review/$referenceCode')
  })

  it('asks the server for the document-health filter instead of narrowing the page it already has', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch({ [COMPLIANCE]: envelope([EXPIRED, HEALTHY]) }, recorded)

    renderPage(<ComplianceDirectoryPage />)
    await screen.findByText('Lapsed Co')

    await userEvent.click(screen.getByRole('combobox', { name: /document health/i }))
    await userEvent.click(await screen.findByRole('option', { name: 'Needs attention' }))

    expect(recorded.some((r) => r.url.includes('documentHealth=attention'))).toBe(true)
  })

  it('renders an em dash for a supplier with no lifecycle state yet', async () => {
    // SupplierLifecycleState.None has no §7.1 row, so there is no label to render - and a chip reading
    // "None" would be the product inventing a status the document does not define.
    restore = mockFetch({ [COMPLIANCE]: envelope([{ ...HEALTHY, onboardingState: 'Submitted', lifecycleState: 'None' }]) })

    renderPage(<ComplianceDirectoryPage />)

    expect(await screen.findByText('Healthy Co')).toBeInTheDocument()
    expect(screen.queryByText('None')).not.toBeInTheDocument()
  })
})
