import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import { renderPage, mockFetch } from '../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, Link: 'a' }
})

const { MyProposalsPage } = await import('./MyProposalsPage')

function proposal(overrides: Record<string, unknown> = {}) {
  return {
    proposalCode: 'PRP-2026-000001',
    rfqCode: 'RFQ-2026-000006',
    rfqTitleEn: 'Catering RFQ',
    rfqTitleAr: 'طلب تموين',
    state: 'Submitted',
    submissionDeadline: '2026-10-01T12:00:00Z',
    totalValue: 125000,
    currencyCode: 'OMR',
    ...overrides,
  }
}

/**
 * SCR-150. The supplier's index of their own bids. Two rules are worth holding: a DRAFT offers
 * "continue" rather than "open", and a supplier sees their OWN price in every state - the
 * two-envelope seal governs what the BUYER may see, not whether a supplier can read their own bid.
 */
describe('MyProposalsPage (SCR-150)', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('lists a proposal with its codes, state and total', async () => {
    restore = mockFetch({ '/api/v1/proposals': [proposal()] })

    renderPage(<MyProposalsPage />)

    expect(await screen.findByText('PRP-2026-000001')).toBeInTheDocument()
    expect(screen.getByText('RFQ-2026-000006')).toBeInTheDocument()
    expect(screen.getByText(/125,000\.00 OMR/)).toBeInTheDocument()
  })

  it('shows a sealed-state proposal its own price', async () => {
    // UnderEvaluation is a state in which the BUYER cannot see this number. The supplier can: the
    // seal is about disclosure to the other side, and hiding a bid from the person who wrote it
    // would be a bug that looks like a security feature.
    restore = mockFetch({ '/api/v1/proposals': [proposal({ state: 'UnderEvaluation' })] })

    renderPage(<MyProposalsPage />)

    expect(await screen.findByText(/125,000\.00 OMR/)).toBeInTheDocument()
  })

  it('labels a draft "continue" and a submitted proposal "open"', async () => {
    restore = mockFetch({
      '/api/v1/proposals': [proposal({ proposalCode: 'PRP-1', state: 'Draft' }), proposal({ proposalCode: 'PRP-2', state: 'Submitted' })],
    })

    renderPage(<MyProposalsPage />)

    expect(await screen.findByRole('button', { name: /continue|متابعة/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /^open|فتح/i })).toBeInTheDocument()
  })

  it('renders a proposal with no deadline and no total rather than showing NaN', async () => {
    // A draft on an RFQ whose window has not opened has neither. Formatting null through the number
    // formatter is how "NaN" reaches a screen.
    restore = mockFetch({ '/api/v1/proposals': [proposal({ state: 'Draft', submissionDeadline: null, totalValue: null, currencyCode: null })] })

    renderPage(<MyProposalsPage />)

    await screen.findByText('PRP-2026-000001')
    expect(screen.queryByText(/NaN/)).not.toBeInTheDocument()
    expect(screen.getAllByText('—').length).toBeGreaterThanOrEqual(2)
  })

  it('says the list is empty rather than rendering an empty table', async () => {
    restore = mockFetch({ '/api/v1/proposals': [] })

    renderPage(<MyProposalsPage />)

    expect(await screen.findByText(/have not|لم تقدّم|no proposals/i)).toBeInTheDocument()
  })

  it('offers a retry when the list cannot be read', async () => {
    restore = mockFetch({ '/api/v1/proposals': { __status: 500 } })

    renderPage(<MyProposalsPage />)

    expect(await screen.findByRole('button', { name: /try again|إعادة المحاولة/i })).toBeInTheDocument()
  })
})
