import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch } from '../../test/renderPage'
import type { Comparison } from '../../api/comparison'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, useParams: () => ({ referenceCode: 'RFQ-2026-000001' }) }
})

const { ComparisonPage } = await import('./ComparisonPage')

function comparisonFixture(overrides: Partial<Comparison> = {}): Comparison {
  return {
    rfqReferenceCode: 'RFQ-2026-000001', rfqTitleAr: 'طلب', rfqTitleEn: 'Sample RFQ', evaluationState: 'NotStarted',
    rfqItems: [{ id: 'item-1', lineNo: 1, titleAr: 'بند', titleEn: 'Widget', quantity: 10, unitOfMeasureCode: 'unit' }],
    proposals: [
      {
        proposalReferenceCode: 'PRP-2026-000001', supplierId: 'sup-a', supplierDisplayNameAr: 'أ', supplierDisplayNameEn: 'Supplier A',
        currencyCode: 'SYP', paymentTerms: 'Net 30', incotermCode: 'FOB', deliveryTermsAr: null, deliveryTermsEn: null,
        warranty: null, validityEnd: '2026-12-01', submittedAt: '2026-08-01T00:00:00Z',
        requirements: [{ requirementId: 'req-1', textAr: 'شرط', textEn: 'Mandatory Requirement', isMandatory: true, answered: true }],
        items: null, grandTotal: null, technicallyQualified: null, technicalWeightedScore: null, financialWeightedScore: null,
        weightedTotal: null, rank: null, tieUnresolved: false, tieResolutionReason: null, criterionScores: null,
      },
      {
        proposalReferenceCode: 'PRP-2026-000002', supplierId: 'sup-b', supplierDisplayNameAr: 'ب', supplierDisplayNameEn: 'Supplier B',
        currencyCode: 'SYP', paymentTerms: 'Net 60', incotermCode: 'CIF', deliveryTermsAr: null, deliveryTermsEn: null,
        warranty: null, validityEnd: '2026-11-01', submittedAt: '2026-08-01T00:00:00Z',
        requirements: [{ requirementId: 'req-1', textAr: 'شرط', textEn: 'Mandatory Requirement', isMandatory: true, answered: false }],
        items: null, grandTotal: null, technicallyQualified: null, technicalWeightedScore: null, financialWeightedScore: null,
        weightedTotal: null, rank: null, tieUnresolved: false, tieResolutionReason: null, criterionScores: null,
      },
    ],
    ...overrides,
  }
}

/** FEAT-12.4/FR-CMP-004: the blindness/two-envelope gate proven against the UI's OWN rendered data,
 * not just "the component renders" - a fixture with evaluationState pre-Consolidated and every
 * proposal's items/criterionScores explicitly null must never render a price or score cell, and a
 * fixture with a disqualified proposal at Consolidated+ must show that one proposal's price cells
 * as absent while a qualified sibling's are shown. */
describe('ComparisonPage', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('empty: shows the no-proposals message', async () => {
    restore = mockFetch({ '/api/v1/rfqs/RFQ-2026-000001/comparison': comparisonFixture({ proposals: [] }) })

    renderPage(<ComparisonPage />)

    expect(await screen.findByText('No proposals submitted yet')).toBeInTheDocument()
  })

  it('pre-consolidation: renders requirement fulfilment but no price, grand total, or score cell for any proposal', async () => {
    restore = mockFetch({ '/api/v1/rfqs/RFQ-2026-000001/comparison': comparisonFixture() })

    renderPage(<ComparisonPage />)

    expect(await screen.findByText('Awaiting evaluation consolidation')).toBeInTheDocument()
    expect(screen.getByText('Supplier A')).toBeInTheDocument()
    expect(screen.getByText('Supplier B')).toBeInTheDocument()

    // Every commercial cell must read "Not visible" - not a price, not a blank, not a zero.
    const notVisibleCells = screen.getAllByText('Not visible')
    expect(notVisibleCells.length).toBeGreaterThanOrEqual(2) // unit price row x 2 proposals

    // No evaluation group at all pre-consolidation.
    expect(screen.queryByText('Evaluation')).not.toBeInTheDocument()
    expect(screen.queryByText('Weighted total')).not.toBeInTheDocument()

    // Requirement fulfilment IS shown (not gated - only pricing/scores are).
    expect(screen.getByText('Met')).toBeInTheDocument()
    expect(screen.getByText('Not met')).toBeInTheDocument()
  })

  it('consolidated: a qualified proposal shows pricing and scores; a disqualified sibling shows neither', async () => {
    restore = mockFetch({
      '/api/v1/rfqs/RFQ-2026-000001/comparison': comparisonFixture({
        evaluationState: 'Consolidated',
        proposals: [
          {
            proposalReferenceCode: 'PRP-2026-000001', supplierId: 'sup-a', supplierDisplayNameAr: 'أ', supplierDisplayNameEn: 'Supplier A',
            currencyCode: 'SYP', paymentTerms: 'Net 30', incotermCode: 'FOB', deliveryTermsAr: null, deliveryTermsEn: null,
            warranty: null, validityEnd: '2026-12-01', submittedAt: '2026-08-01T00:00:00Z',
            requirements: [{ requirementId: 'req-1', textAr: 'شرط', textEn: 'Mandatory Requirement', isMandatory: true, answered: true }],
            items: [{ rfqItemId: 'item-1', quantity: 10, unitPrice: 5, discount: null, lineTotal: 50 }], grandTotal: 50,
            technicallyQualified: true, technicalWeightedScore: 80, financialWeightedScore: 30, weightedTotal: 110, rank: 1, tieUnresolved: false, tieResolutionReason: null,
            criterionScores: [{ criterionId: 'crit-1', nameAr: 'جودة', nameEn: 'Quality', isFinancial: false, weight: 60, maxScore: 100, threshold: 60, averageScore: 85, metThreshold: true }],
          },
          {
            proposalReferenceCode: 'PRP-2026-000002', supplierId: 'sup-b', supplierDisplayNameAr: 'ب', supplierDisplayNameEn: 'Supplier B',
            currencyCode: 'SYP', paymentTerms: 'Net 60', incotermCode: 'CIF', deliveryTermsAr: null, deliveryTermsEn: null,
            warranty: null, validityEnd: '2026-11-01', submittedAt: '2026-08-01T00:00:00Z',
            requirements: [{ requirementId: 'req-1', textAr: 'شرط', textEn: 'Mandatory Requirement', isMandatory: true, answered: false }],
            items: null, grandTotal: null,
            technicallyQualified: false, technicalWeightedScore: 20, financialWeightedScore: null, weightedTotal: null, rank: null, tieUnresolved: false, tieResolutionReason: null,
            criterionScores: [{ criterionId: 'crit-1', nameAr: 'جودة', nameEn: 'Quality', isFinancial: false, weight: 60, maxScore: 100, threshold: 60, averageScore: 25, metThreshold: false }],
          },
        ],
      }),
    })

    renderPage(<ComparisonPage />)

    expect(await screen.findByText('Evaluation')).toBeInTheDocument()

    const rows = screen.getAllByRole('row')
    const qualificationRow = rows.find((r) => within(r).queryByText('Technical qualification'))!
    expect(within(qualificationRow).getByText('Qualified')).toBeInTheDocument()
    expect(within(qualificationRow).getByText('Not qualified')).toBeInTheDocument()

    const grandTotalRow = rows.find((r) => within(r).queryByText('Grand total'))!
    // Formatted through the shared formatter now, so the currency travels with the figure instead of
    // being a bare toFixed(2) beside a code. SYP carries no minor units in ICU, hence "SYP 50".
    expect(within(grandTotalRow).getByText(/SYP\s*50/)).toBeInTheDocument()
    expect(within(grandTotalRow).getByText('Not visible')).toBeInTheDocument()

    const weightedTotalRow = rows.find((r) => within(r).queryByText('Weighted total'))!
    expect(within(weightedTotalRow).getByText(/110\.00/)).toBeInTheDocument()
    // The disqualified proposal's weighted total cell is a dash, never a fabricated number.
    expect(within(weightedTotalRow).getAllByText('—').length).toBeGreaterThan(0)
  })

  it('marks a rank that came from an unresolved tie and offers a resolution with a reason', async () => {
    // A-1/BRULE-069. The award flow refuses rank 1 while this marker is set, so the officer has to be
    // able to see the tie and break it here - and must say why, because a tie broken with no stated
    // basis is exactly what the system refused to do.
    const calls: { url: string; method: string; body: string }[] = []
    restore = mockFetch({
      '/api/v1/rfqs/RFQ-2026-000001/comparison': comparisonFixture({
        evaluationState: 'Consolidated',
        proposals: [
          {
            proposalReferenceCode: 'PRP-2026-000001', supplierId: 's-1',
            supplierDisplayNameAr: 'أ', supplierDisplayNameEn: 'Alpha',
            currencyCode: 'SYP', paymentTerms: 'Net 30', incotermCode: 'FOB',
            deliveryTermsAr: null, deliveryTermsEn: null, warranty: null, validityEnd: null,
            submittedAt: '2026-09-01T10:00:00Z', requirements: [], items: [], grandTotal: 1000,
            technicallyQualified: true, technicalWeightedScore: 48, financialWeightedScore: 20, weightedTotal: 68,
            rank: 1, tieUnresolved: true, tieResolutionReason: null, criterionScores: null,
          },
          {
            proposalReferenceCode: 'PRP-2026-000002', supplierId: 's-2',
            supplierDisplayNameAr: 'ب', supplierDisplayNameEn: 'Beta',
            currencyCode: 'SYP', paymentTerms: 'Net 30', incotermCode: 'FOB',
            deliveryTermsAr: null, deliveryTermsEn: null, warranty: null, validityEnd: null,
            submittedAt: '2026-09-01T10:00:00Z', requirements: [], items: [], grandTotal: 1000,
            technicallyQualified: true, technicalWeightedScore: 48, financialWeightedScore: 20, weightedTotal: 68,
            rank: 2, tieUnresolved: true, tieResolutionReason: null, criterionScores: null,
          },
        ],
      }),
      '/api/v1/rfqs/RFQ-2026-000001/evaluation/resolve-tie': {},
    }, calls)

    renderPage(<ComparisonPage />)

    expect(await screen.findByText('A tie in the ranking needs a decision')).toBeInTheDocument()
    expect(screen.getAllByText('Unresolved tie')).toHaveLength(2)

    const reason = screen.getByLabelText('Reason for choosing PRP-2026-000001')
    const buttons = screen.getAllByRole('button', { name: 'Confirm the order' })
    // Disabled until a reason is typed - the guard checked in the direction that refuses.
    expect(buttons[0]).toBeDisabled()

    await userEvent.type(reason, 'Prior delivery record.')
    expect(screen.getAllByRole('button', { name: 'Confirm the order' })[0]).toBeEnabled()
    await userEvent.click(screen.getAllByRole('button', { name: 'Confirm the order' })[0])

    await vi.waitFor(() => expect(calls.some((c) => c.url.includes('resolve-tie'))).toBe(true))
    expect(JSON.parse(calls.find((c) => c.url.includes('resolve-tie'))!.body))
      .toEqual({ proposalCode: 'PRP-2026-000001', reason: 'Prior delivery record.' })
  })

  it('shows no tie panel when nothing is tied', async () => {
    // The control.
    restore = mockFetch({ '/api/v1/rfqs/RFQ-2026-000001/comparison': comparisonFixture({ evaluationState: 'Consolidated' }) })

    renderPage(<ComparisonPage />)

    // The rank row proves the consolidated section rendered at all, which is what makes the absence
    // of the tie panel meaningful rather than vacuous.
    expect(await screen.findByText('Rank')).toBeInTheDocument()
    expect(screen.queryByText('A tie in the ranking needs a decision')).not.toBeInTheDocument()
    expect(screen.queryByText('Unresolved tie')).not.toBeInTheDocument()
  })
})
