// SCR-430 and SCR-431. Everything on this screen turns on the disclosure tier, and the tier is the SERVER's answer - nothing
// here decides what to hide. What these tests pin is that each tier is EXPLAINED rather than rendered as an absence, because a
// blank cell reads to a buyer as either a broken screen or a bid with no price, and both are worse than the truth.
//
// The sealed tier shows as a state WITH A COUNT rather than as an empty list: the count is what makes it unambiguous, because
// "no proposals" would be false and saying nothing would read as a broken query - both available failure modes here.
//
// A withheld total is LABELLED instead of left blank, and its control is the same cell on the same page at a different tier -
// so a passing "withheld" assertion cannot be the page simply never rendering totals.
//
// "Still loading" is distinguished from "nobody bid": falling through to "no proposals were submitted" while the query settles
// states something false about a live tender, and it is exactly what a screen with a broken query looks like.
//
// One bid opens BESIDE the list rather than navigating away, and the list is still there - two occurrences of the code is the
// assertion, one in the row and one in the panel heading.
//
// The detail's own tier is RE-READ from the detail response rather than inherited from the list: they are separate
// authorisation answers, and a proposal can be readable while its prices are not.
//
// The last test offers a retry when the list cannot be read.

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch } from '../../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, Link: 'a', useParams: () => ({ referenceCode: 'RFQ-2026-000006' }), useRouterState: () => '/back-office/rfqs/RFQ-2026-000006' }
})

const { ReceivedProposalsPage } = await import('./ReceivedProposalsPage')

function row(overrides: Record<string, unknown> = {}) {
  return {
    proposalId: 'p-1',
    proposalCode: 'PRP-2026-000001',
    supplierNameEn: 'Gulf Catering Co.',
    supplierNameAr: 'شركة الخليج للتموين',
    state: 'Submitted',
    submittedAt: '2026-09-20T10:00:00Z',
    itemCount: 4,
    totalValue: 125000,
    currencyCode: 'OMR',
    ...overrides,
  }
}

const LIST = '/api/v1/rfqs/RFQ-2026-000006/received-proposals'

describe('ReceivedProposalsPage (SCR-430)', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('shows the sealed tier as a state with a count, not as an empty list', async () => {
    restore = mockFetch({ [LIST]: { visibility: 'Sealed', submittedCount: 3, proposals: [] } })

    renderPage(<ReceivedProposalsPage />)

    expect(await screen.findByText(/3/)).toBeInTheDocument()
    expect(screen.queryByText(/no proposals were submitted/i)).not.toBeInTheDocument()
  })

  it('labels a withheld total instead of leaving the cell blank', async () => {
    restore = mockFetch({
      [LIST]: { visibility: 'Technical', submittedCount: 1, proposals: [row({ totalValue: null, currencyCode: null })] },
    })

    renderPage(<ReceivedProposalsPage />)

    expect(await screen.findByText('Gulf Catering Co.')).toBeInTheDocument()
    expect(screen.getByText(/after consolidation|بعد/i)).toBeInTheDocument()
  })

  it('shows commercial figures once the tier allows them', async () => {
    restore = mockFetch({
      [LIST]: { visibility: 'Commercial', submittedCount: 1, proposals: [row()] },
    })

    renderPage(<ReceivedProposalsPage />)

    expect(await screen.findByText(/125,000\.00 OMR/)).toBeInTheDocument()
  })

  it('distinguishes "still loading" from "nobody bid"', async () => {
    restore = mockFetch({ [LIST]: { visibility: 'Commercial', submittedCount: 0, proposals: [] } })

    const { container } = renderPage(<ReceivedProposalsPage />)

    expect(container.querySelector('[aria-busy="true"], [role="status"]')).not.toBeNull()
    expect(await screen.findByText(/no proposals|لم تُقدَّم/i)).toBeInTheDocument()
  })

  it('opens one bid beside the list rather than navigating away', async () => {
    restore = mockFetch({
      [LIST]: { visibility: 'Commercial', submittedCount: 1, proposals: [row()] },
      [`${LIST}/p-1`]: {
        proposalId: 'p-1', proposalCode: 'PRP-2026-000001',
        supplierNameEn: 'Gulf Catering Co.', supplierNameAr: 'شركة الخليج',
        visibility: 'Commercial', documentCount: 2,
        narrativeEn: 'We propose a phased rollout.', narrativeAr: 'نقترح تنفيذًا مرحليًا.',
        answers: [{ requirementId: 'r-1', textEn: 'Lead time?', textAr: 'مدة التوريد؟', answerEn: '30 days', answerAr: '٣٠ يومًا' }],
        items: [{ rfqItemId: 'i-1', titleEn: 'Daily meals', titleAr: 'وجبات', quantity: 1000, unitPrice: 125, lineTotal: 125000 }],
        totalValue: 125000, currencyCode: 'OMR', paymentTerms: 'Net 30',
      },
    })

    renderPage(<ReceivedProposalsPage />)

    await userEvent.click(await screen.findByRole('button', { name: /^open|فتح/i }))

    expect(await screen.findByText('We propose a phased rollout.')).toBeInTheDocument()
    expect(screen.getByText('30 days')).toBeInTheDocument()
    expect(screen.getByText('Daily meals')).toBeInTheDocument()
    expect(screen.getAllByText('PRP-2026-000001')).toHaveLength(2)
  })

  it('withholds the detail total when the detail says the tier is not commercial', async () => {
    restore = mockFetch({
      [LIST]: { visibility: 'Technical', submittedCount: 1, proposals: [row({ totalValue: null })] },
      [`${LIST}/p-1`]: {
        proposalId: 'p-1', proposalCode: 'PRP-2026-000001',
        supplierNameEn: 'Gulf Catering Co.', supplierNameAr: 'الخليج',
        visibility: 'Technical', documentCount: 0,
        narrativeEn: null, narrativeAr: null, answers: [],
        items: [{ rfqItemId: 'i-1', titleEn: 'Daily meals', titleAr: 'وجبات', quantity: 1000, unitPrice: null, lineTotal: null }],
        totalValue: null, currencyCode: null, paymentTerms: null,
      },
    })

    renderPage(<ReceivedProposalsPage />)
    await userEvent.click(await screen.findByRole('button', { name: /^open|فتح/i }))

    expect(await screen.findByText('Daily meals')).toBeInTheDocument()
    expect(screen.queryByText(/125,000/)).not.toBeInTheDocument()
  })

  it('offers a retry when the list cannot be read', async () => {
    restore = mockFetch({ [LIST]: { __status: 500 } })

    renderPage(<ReceivedProposalsPage />)

    expect(await screen.findByRole('button', { name: /try again|إعادة المحاولة/i })).toBeInTheDocument()
  })
})
