import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch } from '../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, useParams: () => ({ referenceCode: 'RFQ-2026-000001' }), Link: 'a' }
})

const { SupplierProposalPage } = await import('./SupplierProposalPage')

const RFQ_FIXTURE = {
  rfqCode: 'RFQ-2026-000001', titleAr: 'طلب', titleEn: 'Catering RFQ', descriptionAr: null, descriptionEn: null,
  currencyCode: 'SYP', state: 'SubmissionOpen', submissionOpensAt: null, submissionDeadline: null, clarificationDeadlineAt: null,
  items: [{ id: 'item-1', lineNo: 1, titleAr: 'أ', titleEn: 'Widget', specificationAr: null, specificationEn: null, categoryCode: 'catering', quantity: 5, unitOfMeasureCode: 'unit', isUnitPrice: true, isOptional: false }],
  requirements: [{ id: 'req-1', textAr: 'شرط', textEn: 'Must comply', isMandatory: true, documentTypeCode: null }],
  attachments: [], invitationStatus: 'Invited', clarifications: [], addenda: [],
}

function proposalFixture(state: string, overrides: Record<string, unknown> = {}) {
  return {
    proposalCode: 'PRP-2026-000001', rfqCode: 'RFQ-2026-000001', state,
    createdAt: '2026-08-30T09:00:00Z', totals: { currency: null, grandTotal: 0 },
    currency: null, paymentTerms: null, incotermCode: null, deliveryTermsAr: null, deliveryTermsEn: null,
    warranty: null, validityStart: null, validityEnd: null, narrativeAr: null, narrativeEn: null,
    submittedAt: null, withdrawnAt: null, withdrawReason: null,
    items: [], documents: [], requirementAnswers: [],
    ...overrides,
  }
}

/** FEAT-09.1..09.6: OQ-009 two-envelope note - this page is the owning supplier's own view, the
 * one place both envelopes render together, since it is their own bid. */
describe('SupplierProposalPage', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('shows a Start proposal button when no proposal exists yet', async () => {
    // getProposal 404s via a custom fetch override since mockFetch always returns 200.
    const original = globalThis.fetch
    globalThis.fetch = (async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
      // §12-A/C2: the proposal is discovered at /rfqs/{rfqCode}/proposals now.
      if (url.endsWith('/proposals')) return new Response(null, { status: 404 })
      if (url.includes('/api/v1/rfqs/RFQ-2026-000001')) return new Response(JSON.stringify(RFQ_FIXTURE), { status: 200 })
      throw new Error(`No mock declared for ${url}`)
    }) as typeof fetch
    restore = () => { globalThis.fetch = original }

    renderPage(<SupplierProposalPage />)

    expect(await screen.findByRole('button', { name: 'Start proposal' })).toBeInTheDocument()
  })


  it('a 412 opens the SCR-151 reconcile dialog rather than a toast', async () => {
    // SCR-151: "*Concurrency conflict:* Dialog 'This proposal changed in another tab/user' →
    // reload/merge". §8.1 delivers that case as 412 ETAG_MISMATCH.
    const original = globalThis.fetch
    globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
      const method = (init?.method ?? 'GET').toUpperCase()

      if (method === 'PATCH') {
        return new Response(JSON.stringify({
          type: 'https://api.mots-portal.sy/errors/precondition-failed',
          title: 'The precondition failed.', status: 412, code: 'ETAG_MISMATCH',
        }), { status: 412, headers: { 'Content-Type': 'application/problem+json' } })
      }
      if (url.endsWith('/rfqs/RFQ-2026-000001/proposals')) return new Response(JSON.stringify(proposalFixture('Draft')), { status: 200 })
      if (url.includes('/api/v1/proposals/PRP-2026-000001')) return new Response(JSON.stringify(proposalFixture('Draft')), { status: 200 })
      if (url.includes('/api/v1/rfqs/RFQ-2026-000001')) return new Response(JSON.stringify(RFQ_FIXTURE), { status: 200 })
      throw new Error(`No mock declared for ${url}`)
    }) as typeof fetch
    restore = () => { globalThis.fetch = original }

    renderPage(<SupplierProposalPage />)

    const price = await screen.findByLabelText('Unit price - Widget')
    await userEvent.type(price, '25')
    await userEvent.click(screen.getByRole('button', { name: 'Save price' }))

    expect(await screen.findByText('This proposal changed somewhere else')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Reload' })).toBeInTheDocument()
  })

  it('the Save price button is disabled until a price is entered', async () => {
    // A blank input used to be coerced to 0 and sent, which recorded a free bid before §7.2's rule
    // and produces an unexplained 422 after it.
    restore = mockFetch({
      '/api/v1/rfqs/RFQ-2026-000001/proposals': proposalFixture('Draft'),
      '/api/v1/proposals/PRP-2026-000001': proposalFixture('Draft'),
      '/api/v1/rfqs/RFQ-2026-000001': RFQ_FIXTURE,
    })

    renderPage(<SupplierProposalPage />)

    const save = await screen.findByRole('button', { name: 'Save price' })
    expect(save).toBeDisabled()

    await userEvent.type(screen.getByLabelText('Unit price - Widget'), '25')
    expect(save).toBeEnabled()
  })

  it('Draft: shows the RFQ item for pricing, and saving a price shows a success toast', async () => {
    restore = mockFetch({
      '/api/v1/rfqs/RFQ-2026-000001/proposals': proposalFixture('Draft'),
      // §12-A/C2: mutations address the proposal by its own code.
      '/api/v1/proposals/PRP-2026-000001': proposalFixture('Draft'),
      '/api/v1/rfqs/RFQ-2026-000001': RFQ_FIXTURE,
    })

    renderPage(<SupplierProposalPage />)

    expect(await screen.findByText('Widget')).toBeInTheDocument()
    await userEvent.type(screen.getByLabelText('Unit price - Widget'), '5')
    await userEvent.click(screen.getByRole('button', { name: 'Save price' }))

    expect(await screen.findByText('Item price saved')).toBeInTheDocument()
  })

  it('Draft: answering a requirement shows a success toast', async () => {
    restore = mockFetch({
      '/api/v1/rfqs/RFQ-2026-000001/proposals': proposalFixture('Draft'),
      // §12-A/C2: mutations address the proposal by its own code.
      '/api/v1/proposals/PRP-2026-000001': proposalFixture('Draft'),
      '/api/v1/rfqs/RFQ-2026-000001': RFQ_FIXTURE,
    })

    renderPage(<SupplierProposalPage />)

    await userEvent.type(await screen.findByLabelText('Text (English) - Must comply'), 'Yes')
    await userEvent.type(screen.getByLabelText('Text (Arabic) - Must comply'), 'نعم')
    await userEvent.click(screen.getByRole('button', { name: 'Save answer' }))

    expect(await screen.findByText('Answer saved')).toBeInTheDocument()
  })

  it('Draft: submitting shows a success toast', async () => {
    restore = mockFetch({
      '/api/v1/rfqs/RFQ-2026-000001/proposals': proposalFixture('Draft'),
      // §12-A/C2: mutations address the proposal by its own code.
      '/api/v1/proposals/PRP-2026-000001': proposalFixture('Draft'),
      '/api/v1/rfqs/RFQ-2026-000001': RFQ_FIXTURE,
    })

    renderPage(<SupplierProposalPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Submit proposal' }))

    expect(await screen.findByText('Proposal submitted')).toBeInTheDocument()
  })

  it('Submitted: pricing/answer inputs are gone (state-gated editing) and withdraw is available', async () => {
    restore = mockFetch({
      '/api/v1/rfqs/RFQ-2026-000001/proposals': proposalFixture('Submitted', {
        items: [{ id: 'pi-1', rfqItemId: 'item-1', quantity: 5, unitPrice: 10, discount: null, lineTotal: 50, leadTimeDays: null, notesAr: null, notesEn: null }],
      }),
      // §12-A/C2: mutations address the proposal by its own code.
      '/api/v1/proposals/PRP-2026-000001': proposalFixture('Draft'),
      '/api/v1/rfqs/RFQ-2026-000001': RFQ_FIXTURE,
    })

    renderPage(<SupplierProposalPage />)

    // The fixture proposal has no currency yet, so the line total renders as a bare amount.
    expect(await screen.findByText('50')).toBeInTheDocument()
    expect(screen.queryByLabelText('Unit price - Widget')).not.toBeInTheDocument()
    await userEvent.type(screen.getByLabelText('Reason'), 'Pricing error')
    await userEvent.click(screen.getByRole('button', { name: 'Withdraw proposal' }))

    expect(await screen.findByText('Proposal withdrawn')).toBeInTheDocument()
  })

  it('Withdrawn: withdraw action is hidden', async () => {
    restore = mockFetch({
      '/api/v1/rfqs/RFQ-2026-000001/proposals': proposalFixture('Withdrawn'),
      // §12-A/C2: mutations address the proposal by its own code.
      '/api/v1/proposals/PRP-2026-000001': proposalFixture('Draft'),
      '/api/v1/rfqs/RFQ-2026-000001': RFQ_FIXTURE,
    })

    renderPage(<SupplierProposalPage />)

    await screen.findByText('Withdrawn')
    expect(screen.queryByRole('button', { name: 'Withdraw proposal' })).not.toBeInTheDocument()
  })
})
