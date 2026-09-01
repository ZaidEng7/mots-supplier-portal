import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import { renderPage, mockFetch } from '../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, Link: 'a' }
})

const { SupplierRfqListPage } = await import('./SupplierRfqListPage')

/** FEAT-08.6/FR-INV-006: this list is itself invitation-scoped server-side - the page renders
 * whatever /api/v1/suppliers/me/rfqs returns without any client-side visibility filtering. */
describe('SupplierRfqListPage', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('shows the empty state when invited to nothing', async () => {
    restore = mockFetch({ '/api/v1/suppliers/me/rfqs': [] })

    renderPage(<SupplierRfqListPage />)

    expect(await screen.findByText('No invitations yet')).toBeInTheDocument()
  })

  it('lists invited RFQs with reference, title, and my invitation status', async () => {
    restore = mockFetch({
      '/api/v1/suppliers/me/rfqs': [
        {
          referenceCode: 'RFQ-2026-000001', titleAr: 'طلب', titleEn: 'Catering RFQ', descriptionAr: null, descriptionEn: null,
          currencyCode: 'SYP', state: 'Published', submissionOpensAt: null, submissionClosesAt: null, clarificationDeadlineAt: null,
          items: [], requirements: [], attachments: [], myInvitationStatus: 'Invited',
        },
      ],
    })

    renderPage(<SupplierRfqListPage />)

    expect(await screen.findByText('RFQ-2026-000001')).toBeInTheDocument()
    expect(screen.getByText('Catering RFQ')).toBeInTheDocument()
    expect(screen.getByText('Invited')).toBeInTheDocument()
  })
})
