// FEAT-09.1 through 09.6. OQ-009's two-envelope note applies: this page is the owning supplier's own view, the one place
// both envelopes render together, since it is their own bid.
//
// With no proposal yet the page offers Start. That test makes getProposal 404 through a custom fetch override, because
// mockFetch always returns 200, and the proposal is discovered at /rfqs/{rfqCode}/proposals per §12-A/C2 - which is also
// why every mutation in these tests addresses the proposal by its OWN code.
//
// A 412 opens SCR-151's reconcile dialog rather than a toast: "Concurrency conflict: Dialog 'This proposal changed in
// another tab/user' -> reload/merge", which §8.1 delivers as 412 ETAG_MISMATCH.
//
// The Save price button is disabled until a price is entered. A blank input used to be coerced to 0 and sent, which
// recorded a free bid before §7.2's rule and produces an unexplained 422 after it.
//
// In Draft: the RFQ item shows for pricing and saving a price toasts, answering a requirement toasts, and submitting
// toasts.
//
// Once Submitted the pricing and answer inputs are gone - state-gated editing - and withdraw is available. The fixture
// proposal has no currency yet, so the line total renders as a bare amount. Withdrawn is terminal, per Proposal.cs, and
// this control used to be an inline field beside a `ghost` button - the LOWEST-emphasis variant in the system - with
// nothing anywhere saying the action could not be taken back. It opens a dialog that says so, and the dialog has to say
// the bid really is gone for good. It must NOT say the supplier is out of the tender, because they are not: that dialog
// read "You cannot re-enter this tender" while ProposalHandlers.cs treats a withdrawn proposal as the absence of one and
// creates a new draft - the code's own note records that a supplier who withdrew to correct a price could once never bid
// again, which is the bug that guard exists to stop, and the words were never corrected with it. A Rams audit found the
// pair still disagreeing.
//
// Closing the withdraw dialog withdraws nothing, and the proposal is still there: a dialog that can be dismissed by
// accident and still fires is worse than no dialog, because it looks like a safeguard. Once Withdrawn the action is
// hidden.
//
// T-064's DECLINE control appears on an AwardOffered proposal and not on a Draft one. An AwardOffered proposal the supplier
// cannot act on is the T-067 defect shape again: a state the product can reach and the persona it concerns has no control
// for. It is disabled until a reason is given, because the server requires one and the UI must not invite a request it
// will refuse. The Draft case is the control that the block is state-gated rather than always rendered, and it awaits a
// control the Draft state definitely renders first, so the absence assertion runs against a loaded page rather than a
// skeleton - which is how the first version of it passed for the wrong reason.
//
// A-2 both ways. The supplier can tag the envelope, defaulting to commercial: the knowledge of what a file contains sits
// with whoever attaches it, and Commercial is the default because a mis-tag then under-serves the evaluator rather than
// leaking a price into the technical envelope, which is the direction that fails closed. And the screen tells the supplier
// which envelope the buyer EXPECTS for a requirement - A-2's other half, because the supplier had a picker and nothing to
// tag against.

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, within } from '@testing-library/react'
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
  requirements: [{ id: 'req-1', textAr: 'شرط', textEn: 'Must comply', isMandatory: true, documentTypeCode: null, expectedEnvelope: null }],
  attachments: [], invitationStatus: 'Invited', clarifications: [], addenda: [],
}

function proposalFixture(state: string, overrides: Record<string, unknown> = {}) {
  return {
    proposalCode: 'PRP-2026-000001', rfqCode: 'RFQ-2026-000001', state,
    createdAt: '2026-08-30T09:00:00Z', totals: { currency: null, grandTotal: 0 }, validityDays: null,
    currency: null, paymentTerms: null, incotermCode: null, deliveryTermsAr: null, deliveryTermsEn: null,
    warranty: null, validityStart: null, validityEnd: null, narrativeAr: null, narrativeEn: null,
    submittedAt: null, withdrawnAt: null, withdrawReason: null,
    items: [], documents: [], requirementAnswers: [],
    ...overrides,
  }
}

describe('SupplierProposalPage', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('shows a Start proposal button when no proposal exists yet', async () => {
    const original = globalThis.fetch
    globalThis.fetch = (async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
      if (url.endsWith('/proposals')) return new Response(null, { status: 404 })
      if (url.includes('/api/v1/rfqs/RFQ-2026-000001')) return new Response(JSON.stringify(RFQ_FIXTURE), { status: 200 })
      throw new Error(`No mock declared for ${url}`)
    }) as typeof fetch
    restore = () => { globalThis.fetch = original }

    renderPage(<SupplierProposalPage />)

    expect(await screen.findByRole('button', { name: 'Start proposal' })).toBeInTheDocument()
  })


  it('a 412 opens the SCR-151 reconcile dialog rather than a toast', async () => {
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
      '/api/v1/proposals/PRP-2026-000001': proposalFixture('Draft'),
      '/api/v1/rfqs/RFQ-2026-000001': RFQ_FIXTURE,
    })

    renderPage(<SupplierProposalPage />)

    expect(await screen.findByText('50')).toBeInTheDocument()
    expect(screen.queryByLabelText('Unit price - Widget')).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Withdraw proposal' }))

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText(/cannot be restored/i)).toBeInTheDocument()
    expect(within(dialog).queryByText(/cannot re-enter/i)).toBeNull()
    expect(within(dialog).getByText(/start a new bid/i)).toBeInTheDocument()

    await userEvent.type(within(dialog).getByLabelText('Reason'), 'Pricing error')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Withdraw proposal' }))

    expect(await screen.findByText('Proposal withdrawn')).toBeInTheDocument()
  })

  it('Submitted: closing the withdraw dialog withdraws nothing', async () => {
    restore = mockFetch({
      '/api/v1/rfqs/RFQ-2026-000001/proposals': proposalFixture('Submitted'),
      '/api/v1/proposals/PRP-2026-000001': proposalFixture('Draft'),
      '/api/v1/rfqs/RFQ-2026-000001': RFQ_FIXTURE,
    })

    renderPage(<SupplierProposalPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Withdraw proposal' }))
    const dialog = await screen.findByRole('dialog')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }))

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(screen.queryByText('Proposal withdrawn')).not.toBeInTheDocument()
  })

  it('Withdrawn: withdraw action is hidden', async () => {
    restore = mockFetch({
      '/api/v1/rfqs/RFQ-2026-000001/proposals': proposalFixture('Withdrawn'),
      '/api/v1/proposals/PRP-2026-000001': proposalFixture('Draft'),
      '/api/v1/rfqs/RFQ-2026-000001': RFQ_FIXTURE,
    })

    renderPage(<SupplierProposalPage />)

    await screen.findByText('Withdrawn')
    expect(screen.queryByRole('button', { name: 'Withdraw proposal' })).not.toBeInTheDocument()
  })

  it('offers a decline control on an AwardOffered proposal, and none on a Draft one', async () => {
    restore = mockFetch({
      '/api/v1/rfqs/RFQ-2026-000001/proposals': proposalFixture('AwardOffered'),
      '/api/v1/proposals/PRP-2026-000001': proposalFixture('AwardOffered'),
      '/api/v1/rfqs/RFQ-2026-000001': RFQ_FIXTURE,
    })

    renderPage(<SupplierProposalPage />)

    expect(await screen.findByText('Award offer')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Decline the award' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Decline the award' })).toBeDisabled()
  })

  it('shows no decline control on a Draft proposal', async () => {
    restore = mockFetch({
      '/api/v1/rfqs/RFQ-2026-000001/proposals': proposalFixture('Draft'),
      '/api/v1/proposals/PRP-2026-000001': proposalFixture('Draft'),
      '/api/v1/rfqs/RFQ-2026-000001': RFQ_FIXTURE,
    })

    renderPage(<SupplierProposalPage />)

    expect(await screen.findByRole('button', { name: 'Save price' })).toBeInTheDocument()
    expect(screen.queryByText('Award offer')).not.toBeInTheDocument()
  })

  it('lets the supplier tag the envelope, defaulting to commercial', async () => {
    const calls: { url: string; method: string; body: string }[] = []
    restore = mockFetch({
      '/api/v1/rfqs/RFQ-2026-000001': RFQ_FIXTURE,
      '/api/v1/rfqs/RFQ-2026-000001/proposals': proposalFixture('Draft'),
      '/api/v1/proposals/PRP-2026-000001': proposalFixture('Draft'),
    }, calls)

    renderPage(<SupplierProposalPage />)

    const picker = await screen.findByLabelText('Envelope')
    expect(picker).toBeInTheDocument()
    expect(screen.getByText('Commercial envelope')).toBeInTheDocument()
  })

  it('tells the supplier which envelope the buyer expects for a requirement', async () => {
    restore = mockFetch({
      '/api/v1/rfqs/RFQ-2026-000001': {
        ...RFQ_FIXTURE,
        requirements: [{
          id: 'req-1', textAr: 'شرط', textEn: 'Provide the technical specification',
          isMandatory: true, documentTypeCode: 'spec', expectedEnvelope: 'Technical',
        }],
      },
      '/api/v1/rfqs/RFQ-2026-000001/proposals': proposalFixture('Draft'),
      '/api/v1/proposals/PRP-2026-000001': proposalFixture('Draft'),
    })

    renderPage(<SupplierProposalPage />)

    expect(await screen.findByText('This document is expected in the technical envelope.')).toBeInTheDocument()
  })
})
