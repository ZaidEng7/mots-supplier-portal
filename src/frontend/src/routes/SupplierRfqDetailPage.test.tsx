import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch } from '../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, useParams: () => ({ referenceCode: 'RFQ-2026-000001' }) }
})

const { SupplierRfqDetailPage } = await import('./SupplierRfqDetailPage')

function fixture(myInvitationStatus: string) {
  return {
    referenceCode: 'RFQ-2026-000001', titleAr: 'طلب', titleEn: 'Catering RFQ', descriptionAr: null, descriptionEn: null,
    currencyCode: 'SYP', state: 'Published', submissionOpensAt: null, submissionClosesAt: null, clarificationDeadlineAt: null,
    items: [{ id: 'item-1', lineNo: 1, titleAr: 'أ', titleEn: 'Widget', specificationAr: null, specificationEn: null, categoryCode: 'catering', quantity: 5, unitOfMeasureCode: 'unit', isUnitPrice: true, isOptional: false }],
    requirements: [], attachments: [], myInvitationStatus,
  }
}

/** FEAT-08.4/08.6/FR-INV-004/006: the supplier's own view - proves the decline flow and that a
 * server-side 404 (not a client filter) is what a non-invited supplier would hit here. */
describe('SupplierRfqDetailPage', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('renders RFQ items and the current invitation status', async () => {
    restore = mockFetch({ '/api/v1/suppliers/me/rfqs/RFQ-2026-000001': fixture('Viewed') })

    renderPage(<SupplierRfqDetailPage />)

    expect(await screen.findByText('Widget')).toBeInTheDocument()
    expect(screen.getByText('Viewed')).toBeInTheDocument()
  })

  it('declining shows a success toast', async () => {
    restore = mockFetch({ '/api/v1/suppliers/me/rfqs/RFQ-2026-000001': fixture('Invited') })

    renderPage(<SupplierRfqDetailPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Decline invitation' }))

    expect(await screen.findByText('Invitation declined')).toBeInTheDocument()
  })

  it('hides the decline action once already declined', async () => {
    restore = mockFetch({ '/api/v1/suppliers/me/rfqs/RFQ-2026-000001': fixture('Declined') })

    renderPage(<SupplierRfqDetailPage />)

    await screen.findByText('Widget')
    expect(screen.queryByRole('button', { name: 'Decline invitation' })).not.toBeInTheDocument()
  })

  it('shows a not-found message for a non-invited supplier (server 404)', async () => {
    const original = globalThis.fetch
    globalThis.fetch = (async () => new Response(null, { status: 404 })) as typeof fetch
    restore = () => { globalThis.fetch = original }

    renderPage(<SupplierRfqDetailPage />)

    expect(await screen.findByText('RFQ not found')).toBeInTheDocument()
  })
})
