import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, within } from '@testing-library/react'
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
        weightedTotal: null, rank: null, criterionScores: null,
      },
      {
        proposalReferenceCode: 'PRP-2026-000002', supplierId: 'sup-b', supplierDisplayNameAr: 'ب', supplierDisplayNameEn: 'Supplier B',
        currencyCode: 'SYP', paymentTerms: 'Net 60', incotermCode: 'CIF', deliveryTermsAr: null, deliveryTermsEn: null,
        warranty: null, validityEnd: '2026-11-01', submittedAt: '2026-08-01T00:00:00Z',
        requirements: [{ requirementId: 'req-1', textAr: 'شرط', textEn: 'Mandatory Requirement', isMandatory: true, answered: false }],
        items: null, grandTotal: null, technicallyQualified: null, technicalWeightedScore: null, financialWeightedScore: null,
        weightedTotal: null, rank: null, criterionScores: null,
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
            technicallyQualified: true, technicalWeightedScore: 80, financialWeightedScore: 30, weightedTotal: 110, rank: 1,
            criterionScores: [{ criterionId: 'crit-1', nameAr: 'جودة', nameEn: 'Quality', isFinancial: false, weight: 60, maxScore: 100, threshold: 60, averageScore: 85, metThreshold: true }],
          },
          {
            proposalReferenceCode: 'PRP-2026-000002', supplierId: 'sup-b', supplierDisplayNameAr: 'ب', supplierDisplayNameEn: 'Supplier B',
            currencyCode: 'SYP', paymentTerms: 'Net 60', incotermCode: 'CIF', deliveryTermsAr: null, deliveryTermsEn: null,
            warranty: null, validityEnd: '2026-11-01', submittedAt: '2026-08-01T00:00:00Z',
            requirements: [{ requirementId: 'req-1', textAr: 'شرط', textEn: 'Mandatory Requirement', isMandatory: true, answered: false }],
            items: null, grandTotal: null,
            technicallyQualified: false, technicalWeightedScore: 20, financialWeightedScore: null, weightedTotal: null, rank: null,
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
    expect(within(grandTotalRow).getByText(/50\.00/)).toBeInTheDocument()
    expect(within(grandTotalRow).getByText('Not visible')).toBeInTheDocument()

    const weightedTotalRow = rows.find((r) => within(r).queryByText('Weighted total'))!
    expect(within(weightedTotalRow).getByText(/110\.00/)).toBeInTheDocument()
    // The disqualified proposal's weighted total cell is a dash, never a fabricated number.
    expect(within(weightedTotalRow).getAllByText('—').length).toBeGreaterThan(0)
  })
})
