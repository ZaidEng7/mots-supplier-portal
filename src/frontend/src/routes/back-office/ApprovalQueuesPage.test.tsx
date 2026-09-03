import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import { renderPage, mockFetch } from '../../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, Link: 'a' }
})

const { ApprovalQueuesPage } = await import('./ApprovalQueuesPage')

describe('ApprovalQueuesPage (SCR-401)', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('renders both queues the inventory names', async () => {
    restore = mockFetch({
      '/api/v1/procurement/approvals': {
        rfqPublishApprovals: [
          { rfqReferenceCode: 'RFQ-1', titleAr: 'طلب', titleEn: 'Catering RFQ', state: 'InternalReview', waitingSince: null, href: '/api/v1/rfqs/RFQ-1' },
        ],
        awardApprovals: [
          { rfqReferenceCode: 'RFQ-2', titleAr: 'ترسية', titleEn: 'Cleaning RFQ', state: 'PendingApproval', waitingSince: '2026-09-01T10:00:00Z', href: '/api/v1/rfqs/RFQ-2/award' },
        ],
      },
    })

    renderPage(<ApprovalQueuesPage />)

    expect(await screen.findByText('RFQs awaiting publish approval')).toBeInTheDocument()
    expect(screen.getByText('Awards awaiting approval')).toBeInTheDocument()
    expect(screen.getByText('Catering RFQ')).toBeInTheDocument()
    expect(screen.getByText('Cleaning RFQ')).toBeInTheDocument()
  })

  it('says the queues belong to the organization, not to the reader', async () => {
    // Nothing resolves a single named approver, so copy claiming "assigned to you" would be a claim
    // the system cannot make.
    restore = mockFetch({
      '/api/v1/procurement/approvals': { rfqPublishApprovals: [], awardApprovals: [] },
    })

    renderPage(<ApprovalQueuesPage />)

    expect(await screen.findByText('Work waiting for approval in your organization.')).toBeInTheDocument()
  })

  it('empty: each queue says so separately', async () => {
    // Separately, because "no approvals" over a merged list would hide which half is empty - and the
    // two queues are worked by different actions.
    restore = mockFetch({
      '/api/v1/procurement/approvals': { rfqPublishApprovals: [], awardApprovals: [] },
    })

    renderPage(<ApprovalQueuesPage />)

    expect(await screen.findByText('No RFQs are waiting for approval')).toBeInTheDocument()
    expect(screen.getByText('No awards are waiting for approval')).toBeInTheDocument()
  })
})
