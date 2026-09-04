import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch } from '../../test/renderPage'
import type { MyEvaluation } from '../../api/evaluations'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, useParams: () => ({ referenceCode: 'RFQ-2026-000001' }) }
})

const { MyEvaluationPage } = await import('./MyEvaluationPage')

function myEvaluationFixture(overrides: Partial<MyEvaluation> = {}): MyEvaluation {
  return {
    rfqReferenceCode: 'RFQ-2026-000001', state: 'InProgress', submittedAt: null,
    // T-067: the specification the bids answer, which an evaluator could not reach from anywhere in
    // the product before this.
    rfqTitleAr: 'طلب تجريبي', rfqTitleEn: 'Catering RFQ',
    rfqDescriptionAr: null, rfqDescriptionEn: null,
    rfqItems: [], rfqRequirements: [],
    criteria: [
      { id: 'crit-tech', nameAr: 'جودة', nameEn: 'Quality', dimension: 'Technical', weight: 60, maxScore: 100, threshold: 60, scoringType: 'Numeric', isFinancial: false },
      { id: 'crit-fin', nameAr: 'سعر', nameEn: 'Price', dimension: 'Commercial', weight: 40, maxScore: 100, threshold: null, scoringType: 'Numeric', isFinancial: true },
    ],
    proposals: [{
      proposalCode: 'PRP-2026-000001',
      supplierReferenceCode: 'SUP-2026-000001',
      supplierDisplayNameAr: 'شركة الاختبار', supplierDisplayNameEn: 'Test Supplies Co',
      narrativeAr: null, narrativeEn: 'We use 300-thread cotton.',
      requirementAnswers: [], documents: [],
      technicallyQualified: false,
    }],
    myScores: [],
    ...overrides,
  }
}

/** FEAT-11.3/FR-EVL-003..005: the two-envelope gate's frontend half - a UI convenience only (hide/
 * disable), never the real gate (that is ScoreCriterionHandler, proven server-side in the backend
 * integration suite). This file proves the financial input is disabled pre-qualification and
 * becomes enabled once the fixture reports the proposal qualified. */
describe('MyEvaluationPage', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('not assigned: shows the not-assigned message instead of a crash', async () => {
    restore = mockFetch({ '/api/v1/rfqs/RFQ-2026-000001/my-evaluation': null })

    renderPage(<MyEvaluationPage />)

    expect(await screen.findByText('You are not assigned to this evaluation')).toBeInTheDocument()
  })

  it('disables the financial score input until the proposal is technically qualified', async () => {
    restore = mockFetch({ '/api/v1/rfqs/RFQ-2026-000001/my-evaluation': myEvaluationFixture() })

    renderPage(<MyEvaluationPage />)

    expect(await screen.findByText('Not technically qualified')).toBeInTheDocument()
    expect(screen.getByLabelText('Score: Price')).toBeDisabled()
    expect(screen.getByLabelText('Score: Quality')).toBeEnabled()
  })

  it('enables the financial score input once qualified, and saving a score shows a success toast', async () => {
    restore = mockFetch({
      '/api/v1/rfqs/RFQ-2026-000001/my-evaluation': myEvaluationFixture({
        proposals: [{
          proposalCode: 'PRP-2026-000001',
          supplierReferenceCode: 'SUP-2026-000001',
          supplierDisplayNameAr: 'شركة الاختبار', supplierDisplayNameEn: 'Test Supplies Co',
          narrativeAr: null, narrativeEn: 'We use 300-thread cotton.',
          requirementAnswers: [], documents: [],
          technicallyQualified: true,
        }],
        myScores: [{ proposalCode: 'PRP-2026-000001', criterionId: 'crit-tech', rawScore: 75, commentAr: null, commentEn: null, scoredAt: '2026-08-01T00:00:00Z' }],
      }),
    })

    renderPage(<MyEvaluationPage />)

    expect(await screen.findByText('Technically qualified')).toBeInTheDocument()
    const financialInput = screen.getByLabelText('Score: Price')
    expect(financialInput).toBeEnabled()

    await userEvent.type(financialInput, '80')
    const saveButtons = screen.getAllByRole('button', { name: 'Save score' })
    await userEvent.click(saveButtons[1])

    expect(await screen.findByText('Score saved')).toBeInTheDocument()
  })

  it('submitted: shows the already-submitted message instead of a submit button', async () => {
    restore = mockFetch({
      '/api/v1/rfqs/RFQ-2026-000001/my-evaluation': myEvaluationFixture({ submittedAt: '2026-08-05T00:00:00Z', state: 'EvaluatorSubmitted' }),
    })

    renderPage(<MyEvaluationPage />)

    expect(await screen.findByText('You have already submitted your evaluation')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Submit evaluation' })).not.toBeInTheDocument()
  })

  it('renders the bid content the evaluator is scoring, which was absent entirely before T-067', async () => {
    // The defect this page had: it printed a proposal GUID as the bid's identity and nothing else.
    restore = mockFetch({ '/api/v1/rfqs/RFQ-2026-000001/my-evaluation': myEvaluationFixture() })

    renderPage(<MyEvaluationPage />)

    expect(await screen.findByText('We use 300-thread cotton.')).toBeInTheDocument()
    expect(screen.getByText('Test Supplies Co')).toBeInTheDocument()
    expect(screen.getByText(/PRP-2026-000001/)).toBeInTheDocument()
    // The specification, on the same screen as the scoring.
    expect(screen.getByText(/Catering RFQ/)).toBeInTheDocument()
  })
})
