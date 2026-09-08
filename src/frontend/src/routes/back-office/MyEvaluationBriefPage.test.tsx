import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import { mockFetch, renderPage } from '../../test/renderPage'
import type { MyEvaluation } from '../../api/evaluations'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, Link: 'a' }
})

const { MyEvaluationBriefPage } = await import('./MyEvaluationBriefPage')

/**
 * SCR-501. The row T-102 left unresolved, and the reason it could not be resolved from the screen alone:
 * each criterion's guidance never reached the evaluator at all.
 */

const EVALUATION = '/api/v1/rfqs/RFQ-2026-000001/my-evaluation'

function evaluation(overrides: Partial<MyEvaluation> = {}): MyEvaluation {
  return {
    rfqReferenceCode: 'RFQ-2026-000001',
    state: 'InProgress',
    rfqTitleAr: 'طلب تموين',
    rfqTitleEn: 'Catering tender',
    rfqDescriptionAr: 'وصف',
    rfqDescriptionEn: 'Hot meals for three sites',
    rfqItems: [],
    rfqRequirements: [
      { id: 'r1', textAr: 'شهادة سلامة غذائية', textEn: 'Food safety certificate', isMandatory: true, documentTypeCode: null },
    ],
    submittedAt: null,
    criteria: [
      {
        id: 'c1', nameAr: 'الجودة', nameEn: 'Quality', dimension: 'Technical', weight: 60, maxScore: 100,
        threshold: 50, scoringType: 'Numeric', isFinancial: false, requiresJustification: true,
        guidanceAr: 'قيّم الجودة وفق المعايير', guidanceEn: 'Score against the stated quality standards',
      },
      {
        id: 'c2', nameAr: 'السعر', nameEn: 'Price', dimension: 'Commercial', weight: 40, maxScore: 100,
        threshold: null, scoringType: 'Numeric', isFinancial: true,
      },
    ],
    proposals: [],
    myScores: [],
    ...overrides,
  } as MyEvaluation
}

let restore: (() => void) | undefined
afterEach(() => restore?.())

describe('MyEvaluationBriefPage', () => {
  it('renders each criterion with the guidance the template author wrote', async () => {
    restore = mockFetch({ [EVALUATION]: evaluation() })

    renderPage(<MyEvaluationBriefPage referenceCode="RFQ-2026-000001" />)

    expect(await screen.findByText('Score against the stated quality standards')).toBeInTheDocument()
    expect(screen.getByText('Quality')).toBeInTheDocument()
    expect(screen.getByText('Hot meals for three sites')).toBeInTheDocument()
  })

  it('says guidance was not recorded, rather than leaving the cell blank', async () => {
    // A tender that bound its template before the snapshot carried guidance has none, and a blank cell
    // reads as a loading failure. The alternative - showing the template's CURRENT text - would show an
    // instruction this tender never bound.
    restore = mockFetch({ [EVALUATION]: evaluation() })

    renderPage(<MyEvaluationBriefPage referenceCode="RFQ-2026-000001" />)

    expect(await screen.findByText('No guidance was recorded for this criterion')).toBeInTheDocument()
  })

  it('totals the weights, because 60 means nothing on its own', async () => {
    restore = mockFetch({ [EVALUATION]: evaluation() })

    renderPage(<MyEvaluationBriefPage referenceCode="RFQ-2026-000001" />)

    expect(await screen.findByText('Weights total: 100')).toBeInTheDocument()
  })

  it('marks the criteria that will refuse a score without a justification', async () => {
    // BRULE-061 is enforced server-side and the scoring form does not read the flag, so this is where an
    // evaluator finds out before their score is refused.
    restore = mockFetch({ [EVALUATION]: evaluation() })

    renderPage(<MyEvaluationBriefPage referenceCode="RFQ-2026-000001" />)

    expect(await screen.findByText('Justification required')).toBeInTheDocument()
  })

  it('shows the mandatory requirements the bids had to answer', async () => {
    restore = mockFetch({ [EVALUATION]: evaluation() })

    renderPage(<MyEvaluationBriefPage referenceCode="RFQ-2026-000001" />)

    expect(await screen.findByText('Food safety certificate')).toBeInTheDocument()
    expect(screen.getByText('Mandatory')).toBeInTheDocument()
  })

  it('says the brief could not be loaded rather than rendering an empty shell', async () => {
    restore = mockFetch({ [EVALUATION]: { __status: 404 } })

    renderPage(<MyEvaluationBriefPage referenceCode="RFQ-2026-000001" />)

    expect(await screen.findByText('Could not load the brief')).toBeInTheDocument()
  })
})
