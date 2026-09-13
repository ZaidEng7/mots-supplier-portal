// FEAT-08.4, 08.6, FR-INV-004 and FR-INV-006: the supplier's own view. It proves the decline flow, and that a server-side
// 404 rather than a client filter is what a non-invited supplier would hit here.
//
// The RFQ items and the current invitation status render; declining shows a success toast; the decline action is hidden
// once already declined; and a non-invited supplier gets a not-found message from the server's own 404.
//
// A PublishedToAll clarification renders with no asker-identity field at all, and asking a new question shows a success
// toast. The asker's own question is marked "My question".
//
// THE ATTACHMENTS are SCR-142's. The payload carried them since EPIC-08 and nothing rendered them, so an invited supplier
// could read the RFQ and never reach the documents it depends on - found by the batch 9 per-screen sweep. The URL is
// short-lived and issued per request (D-16), so it must be fetched on the click rather than rendered into the page where
// it would outlive its own validity. With none, the card says so rather than rendering empty.
//
// The last pair is A-6's deadline reason. The notification cannot carry it - BRULE-091's allow-list is identifiers and
// public codes, and it already refused a DATE on the grounds that a date is content - so the message points here and the
// reason is waiting on the RFQ, beside the deadline it explains. Its control is that no card appears when the deadline
// has not moved.

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch } from '../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, useParams: () => ({ referenceCode: 'RFQ-2026-000001' }), Link: 'a' }
})

const { SupplierRfqDetailPage } = await import('./SupplierRfqDetailPage')

function fixture(invitationStatus: string, overrides: Record<string, unknown> = {}) {
  return {
    rfqCode: 'RFQ-2026-000001', titleAr: 'طلب', titleEn: 'Catering RFQ', descriptionAr: null, descriptionEn: null,
    currencyCode: 'SYP', state: 'Published', submissionOpensAt: null, submissionDeadline: null, clarificationDeadlineAt: null,
    items: [{ id: 'item-1', lineNo: 1, titleAr: 'أ', titleEn: 'Widget', specificationAr: null, specificationEn: null, categoryCode: 'catering', quantity: 5, unitOfMeasureCode: 'unit', isUnitPrice: true, isOptional: false }],
    requirements: [], attachments: [], invitationStatus, clarifications: [], addenda: [],
    submissionDeadlineChangeReason: null, submissionDeadlineChangedAt: null,
    ...overrides,
  }
}

const ATTACHMENT = {
  id: 'att-1',
  originalFileName: 'tender-terms.pdf',
  contentType: 'application/pdf',
  caption: 'Terms of reference',
  uploadedAt: '2026-09-01T10:00:00Z',
}

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

    expect(await screen.findByText('Tender not found')).toBeInTheDocument()
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

  it('lists the tender attachments and downloads one on demand', async () => {
    const open = vi.spyOn(window, 'open').mockImplementation(() => null)
    restore = mockFetch({
      '/api/v1/rfqs/RFQ-2026-000001': fixture('Viewed', { attachments: [ATTACHMENT] }),
      '/api/v1/rfqs/RFQ-2026-000001/attachments/att-1/download-url': { url: 'https://storage.example/signed' },
    })

    renderPage(<SupplierRfqDetailPage />)

    expect(await screen.findByText('tender-terms.pdf')).toBeInTheDocument()
    expect(screen.getByText('Terms of reference')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Download' }))

    await vi.waitFor(() => expect(open).toHaveBeenCalledWith('https://storage.example/signed', '_blank', 'noopener,noreferrer'))
    open.mockRestore()
  })

  it('says there are no attachments rather than rendering an empty card', async () => {
    restore = mockFetch({ '/api/v1/rfqs/RFQ-2026-000001': fixture('Viewed') })

    renderPage(<SupplierRfqDetailPage />)

    expect(await screen.findByText('No attachments')).toBeInTheDocument()
  })

  it('tells the supplier why their deadline moved', async () => {
    restore = mockFetch({
      '/api/v1/rfqs/RFQ-2026-000001': fixture('Viewed', {
        submissionDeadlineChangeReason: 'The Ministry extended the tender period.',
        submissionDeadlineChangedAt: '2026-09-05T10:00:00Z',
      }),
    })

    renderPage(<SupplierRfqDetailPage />)

    expect(await screen.findByText('The submission deadline changed')).toBeInTheDocument()
    expect(screen.getByText('The Ministry extended the tender period.')).toBeInTheDocument()
  })

  it('shows no deadline-change card when the deadline has not moved', async () => {
    restore = mockFetch({ '/api/v1/rfqs/RFQ-2026-000001': fixture('Viewed') })

    renderPage(<SupplierRfqDetailPage />)

    expect(await screen.findByText('No attachments')).toBeInTheDocument()
    expect(screen.queryByText('The submission deadline changed')).not.toBeInTheDocument()
  })
})
