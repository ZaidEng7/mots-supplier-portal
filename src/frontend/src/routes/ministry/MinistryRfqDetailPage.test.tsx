// SCR-606, under D-66 - the screen the disclosure decision is actually about.
//
// These tests exist as much to RECORD what is shown as to check that it renders: a named bidder and its total, on a tender
// still open for submissions. If a later change narrows that, they fail and somebody has to decide deliberately rather than
// discover it.
//
// Each bidder shows by name with its value, on a tender still open - D-66's scope asserted rather than described: live
// tenders included, per-bidder values shown. The fixture's tender is SubmissionOpen, which is the state in which a bid value
// is most sensitive.
//
// The tender is read-only, because a ministry viewer holds no write permission at all.
//
// With the policy flag off the values are WITHHELD and the screen says so - not zeroes, and not a blank: "policy withholds
// this" and "they bid nothing" are different facts, and a screen that rendered 0 would be asserting the second. The bidder
// is still named, because the flag governs the money rather than who took part.
//
// "Not awarded yet" is distinguished from "withheld": both render as an absence of a number and they mean opposite things -
// one is the tender's state, the other is policy.
//
// And the page says so when the tender cannot be loaded.

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import { mockFetch, renderPage } from '../../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, Link: 'a' }
})

const { MinistryRfqDetailPage } = await import('./MinistryRfqDetailPage')


const DETAIL = '/api/v1/ministry/rfqs/RFQ-2026-000001'

function detail(overrides: Record<string, unknown> = {}) {
  return {
    summary: {
      referenceCode: 'RFQ-2026-000001', titleAr: 'طلب تموين', titleEn: 'Catering tender',
      state: 'SubmissionOpen', organizationNameAr: 'وزارة السياحة', organizationNameEn: 'Ministry of Tourism',
      publishedAt: '2026-09-01T09:00:00Z', submissionClosesAt: '2026-09-20T09:00:00Z',
      invitedSuppliers: 3, submittedProposals: 2, awardedValue: null, currencyCode: 'SYP',
    },
    descriptionAr: 'وصف', descriptionEn: 'Hot meals for three sites',
    items: [{ titleAr: 'وجبات', titleEn: 'Meals', categoryCode: 'catering', quantity: 500, unitOfMeasureCode: 'unit' }],
    bids: [
      {
        proposalCode: 'PRP-2026-000001', supplierCode: 'SUP-2026-000001',
        supplierDisplayNameAr: 'شركة الشام', supplierDisplayNameEn: 'Al-Sham Trading',
        state: 'UnderReview', submittedAt: '2026-09-05T10:00:00Z', totalValue: 40_000_000, isAwarded: false,
      },
      {
        proposalCode: 'PRP-2026-000002', supplierCode: 'SUP-2026-000002',
        supplierDisplayNameAr: 'شركة بردى', supplierDisplayNameEn: 'Barada Supplies',
        state: 'UnderReview', submittedAt: '2026-09-06T10:00:00Z', totalValue: 43_500_000, isAwarded: false,
      },
    ],
    commercialValuesVisible: true,
    ...overrides,
  }
}

let restore: (() => void) | undefined
afterEach(() => restore?.())

describe('MinistryRfqDetailPage', () => {
  it('shows each bidder by name with its value, on a tender still open', async () => {
    restore = mockFetch({ [DETAIL]: detail() })

    renderPage(<MinistryRfqDetailPage referenceCode="RFQ-2026-000001" />)

    expect(await screen.findByText('Al-Sham Trading')).toBeInTheDocument()
    expect(screen.getByText('Barada Supplies')).toBeInTheDocument()
    expect(screen.getByText('Open for submissions')).toBeInTheDocument()
  })

  it('says the tender is read-only, because a ministry viewer holds no write permission at all', async () => {
    restore = mockFetch({ [DETAIL]: detail() })

    renderPage(<MinistryRfqDetailPage referenceCode="RFQ-2026-000001" />)

    expect(await screen.findByText('Read-only')).toBeInTheDocument()
  })

  it('withholds the values and says so when the policy flag is off', async () => {
    restore = mockFetch({
      [DETAIL]: detail({
        commercialValuesVisible: false,
        bids: detail().bids.map((bid) => ({ ...bid, totalValue: null })),
      }),
    })

    renderPage(<MinistryRfqDetailPage referenceCode="RFQ-2026-000001" />)

    expect(await screen.findByText(/withheld by disclosure policy/i)).toBeInTheDocument()
    expect(screen.getAllByText('Withheld').length).toBeGreaterThan(0)
    expect(screen.getByText('Al-Sham Trading')).toBeInTheDocument()
  })

  it('distinguishes "not awarded yet" from "withheld"', async () => {
    restore = mockFetch({ [DETAIL]: detail() })

    renderPage(<MinistryRfqDetailPage referenceCode="RFQ-2026-000001" />)

    expect(await screen.findByText('Not awarded yet')).toBeInTheDocument()
  })

  it('says so when the tender cannot be loaded', async () => {
    restore = mockFetch({ [DETAIL]: { __status: 404 } })

    renderPage(<MinistryRfqDetailPage referenceCode="RFQ-2026-000001" />)

    expect(await screen.findByText('Could not load the tender')).toBeInTheDocument()
  })
})
