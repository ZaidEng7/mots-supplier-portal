import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch } from '../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, useParams: () => ({ referenceCode: 'RFQ-2026-000001' }), Link: 'a' }
})

const { SupplierRfqDetailPage } = await import('./SupplierRfqDetailPage')

function fixture(myInvitationStatus: string) {
  return {
    referenceCode: 'RFQ-2026-000001', titleAr: 'طلب', titleEn: 'Catering RFQ', descriptionAr: null, descriptionEn: null,
    currencyCode: 'SYP', state: 'Published', submissionOpensAt: null, submissionClosesAt: null, clarificationDeadlineAt: null,
    items: [{ id: 'item-1', lineNo: 1, titleAr: 'أ', titleEn: 'Widget', specificationAr: null, specificationEn: null, categoryCode: 'catering', quantity: 5, unitOfMeasureCode: 'unit', isUnitPrice: true, isOptional: false }],
    requirements: [], attachments: [], myInvitationStatus, clarifications: [], addenda: [],
  }
}

/** FEAT-08.4/08.6/FR-INV-004/006: the supplier's own view - proves the decline flow and that a
 * server-side 404 (not a client filter) is what a non-invited supplier would hit here. */
describe('SupplierRfqDetailPage', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('renders RFQ items and the current invitation status', async () => {
    restore = mockFetch({ '/api/v1/rfqs/RFQ-2026-000001': fixture('Viewed') })

    renderPage(<SupplierRfqDetailPage />)

    expect(await screen.findByText('Widget')).toBeInTheDocument()
    expect(screen.getByText('Viewed')).toBeInTheDocument()
  })

  it('declining shows a success toast', async () => {
    restore = mockFetch({ '/api/v1/rfqs/RFQ-2026-000001': fixture('Invited') })

    renderPage(<SupplierRfqDetailPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Decline invitation' }))

    expect(await screen.findByText('Invitation declined')).toBeInTheDocument()
  })

  it('hides the decline action once already declined', async () => {
    restore = mockFetch({ '/api/v1/rfqs/RFQ-2026-000001': fixture('Declined') })

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

  it('shows a PublishedToAll clarification without any asker-identity field, and asking a new question shows a success toast', async () => {
    restore = mockFetch({
      '/api/v1/rfqs/RFQ-2026-000001': {
        ...fixture('Viewed'),
        clarifications: [
          { id: 'cl-1', question: 'What is the delivery incoterm?', answer: 'FOB.', visibility: 'PublishedToAll', askedAt: '2026-08-01T00:00:00Z', answeredAt: '2026-08-02T00:00:00Z', isMine: false },
        ],
      },
    })

    renderPage(<SupplierRfqDetailPage />)

    expect(await screen.findByText('What is the delivery incoterm?')).toBeInTheDocument()
    expect(screen.getByText('FOB.')).toBeInTheDocument()
    expect(screen.queryByText('My question')).not.toBeInTheDocument()

    await userEvent.type(screen.getByLabelText('Type your question…'), 'Another question?')
    await userEvent.click(screen.getByRole('button', { name: 'Send question' }))

    expect(await screen.findByText('Question sent')).toBeInTheDocument()
  })

  it('marks the asker’s own question as "My question"', async () => {
    restore = mockFetch({
      '/api/v1/rfqs/RFQ-2026-000001': {
        ...fixture('Viewed'),
        clarifications: [
          { id: 'cl-1', question: 'My own question', answer: null, visibility: 'PrivateToAsker', askedAt: '2026-08-01T00:00:00Z', answeredAt: null, isMine: true },
        ],
      },
    })

    renderPage(<SupplierRfqDetailPage />)

    expect(await screen.findByText('My own question')).toBeInTheDocument()
    expect(screen.getByText('My question')).toBeInTheDocument()
    expect(screen.getByText('Awaiting answer')).toBeInTheDocument()
  })
})
