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

/** A-8: this evaluator has already declared, so the window is shut and the workspace is what renders. */
const DECLARED = { declarationRequired: false, bidders: [] }

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
      // A-8: the default fixture models what the SERVER sends while scoring is open - the pseudonym,
      // and NULL for every identity field. A fixture carrying a name here would be a shape the API
      // cannot produce, and the test would prove nothing about the screen's real input.
      bidderLabelAr: 'مورّد أ', bidderLabelEn: 'Bidder A',
      supplierReferenceCode: null,
      supplierDisplayNameAr: null, supplierDisplayNameEn: null,
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
    restore = mockFetch({
      // A-8: declared explicitly. Without it the longest-match in mockFetch answers the bidders read
      // with the my-evaluation fixture, whose `declarationRequired` is undefined - the page then works
      // for an accidental reason, which is not a test.
      '/api/v1/rfqs/RFQ-2026-000001/my-evaluation/bidders': DECLARED,
      '/api/v1/rfqs/RFQ-2026-000001/my-evaluation': myEvaluationFixture(),
    })

    renderPage(<MyEvaluationPage />)

    expect(await screen.findByText('Not technically qualified')).toBeInTheDocument()
    expect(screen.getByLabelText('Score: Price')).toBeDisabled()
    expect(screen.getByLabelText('Score: Quality')).toBeEnabled()
  })

  it('enables the financial score input once qualified, and saving a score shows a success toast', async () => {
    restore = mockFetch({
      '/api/v1/rfqs/RFQ-2026-000001/my-evaluation/bidders': DECLARED,
      '/api/v1/rfqs/RFQ-2026-000001/my-evaluation': myEvaluationFixture({
        proposals: [{
          proposalCode: 'PRP-2026-000001',
          bidderLabelAr: 'مورّد أ', bidderLabelEn: 'Bidder A',
          supplierReferenceCode: null,
          supplierDisplayNameAr: null, supplierDisplayNameEn: null,
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
      '/api/v1/rfqs/RFQ-2026-000001/my-evaluation/bidders': DECLARED,
      '/api/v1/rfqs/RFQ-2026-000001/my-evaluation': myEvaluationFixture({ submittedAt: '2026-08-05T00:00:00Z', state: 'EvaluatorSubmitted' }),
    })

    renderPage(<MyEvaluationPage />)

    expect(await screen.findByText('You have already submitted your evaluation')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Submit evaluation' })).not.toBeInTheDocument()
  })

  it('renders the bid content the evaluator is scoring, which was absent entirely before T-067', async () => {
    // The defect this page had: it printed a proposal GUID as the bid's identity and nothing else.
    restore = mockFetch({
      // A-8: declared explicitly. Without it the longest-match in mockFetch answers the bidders read
      // with the my-evaluation fixture, whose `declarationRequired` is undefined - the page then works
      // for an accidental reason, which is not a test.
      '/api/v1/rfqs/RFQ-2026-000001/my-evaluation/bidders': DECLARED,
      '/api/v1/rfqs/RFQ-2026-000001/my-evaluation': myEvaluationFixture(),
    })

    renderPage(<MyEvaluationPage />)

    expect(await screen.findByText('We use 300-thread cotton.')).toBeInTheDocument()
    // A-8: the bid is identified by its pseudonym while scoring is open, not by its owner.
    expect(screen.getByText('Bidder A')).toBeInTheDocument()
    expect(screen.getByText(/PRP-2026-000001/)).toBeInTheDocument()
    // The specification, on the same screen as the scoring.
    expect(screen.getByText(/Catering RFQ/)).toBeInTheDocument()
  })

  it('withholds the bidder identity while scoring is open', async () => {
    // A-8, and this supersedes D-19. The evaluator sees a stable pseudonym and is told plainly that the
    // identity is withheld, rather than being left to wonder whether the data is missing.
    restore = mockFetch({
      // A-8: declared explicitly. Without it the longest-match in mockFetch answers the bidders read
      // with the my-evaluation fixture, whose `declarationRequired` is undefined - the page then works
      // for an accidental reason, which is not a test.
      '/api/v1/rfqs/RFQ-2026-000001/my-evaluation/bidders': DECLARED,
      '/api/v1/rfqs/RFQ-2026-000001/my-evaluation': myEvaluationFixture(),
    })

    renderPage(<MyEvaluationPage />)

    expect(await screen.findByText('Bidder A')).toBeInTheDocument()
    expect(screen.getByText('Bidder identity withheld during scoring')).toBeInTheDocument()
    expect(screen.queryByText(/Test Supplies Co/)).not.toBeInTheDocument()
  })

  it('names the bidder once the identity is revealed', async () => {
    // The control. The server decides when - before scoring opens, which is BRULE-067's recusal
    // declaration, and after consolidation, when the scores are locked - and the screen renders
    // whichever it is given. Both labels show, so a comment written under the pseudonym still reads.
    restore = mockFetch({
      '/api/v1/rfqs/RFQ-2026-000001/my-evaluation/bidders': DECLARED,
      '/api/v1/rfqs/RFQ-2026-000001/my-evaluation': myEvaluationFixture({
        state: 'Consolidated',
        proposals: [{
          proposalCode: 'PRP-2026-000001',
          bidderLabelAr: 'مورّد أ', bidderLabelEn: 'Bidder A',
          supplierReferenceCode: 'SUP-2026-000001',
          supplierDisplayNameAr: 'شركة الاختبار', supplierDisplayNameEn: 'Test Supplies Co',
          narrativeAr: null, narrativeEn: 'We use 300-thread cotton.',
          requirementAnswers: [], documents: [],
          technicallyQualified: true,
        }],
      }),
    })

    renderPage(<MyEvaluationPage />)

    expect(await screen.findByText(/Test Supplies Co/)).toBeInTheDocument()
    expect(screen.getByText('Bidder A')).toBeInTheDocument()
    expect(screen.queryByText('Bidder identity withheld during scoring')).not.toBeInTheDocument()
  })

  it('asks for a conflict declaration before it will load the workspace', async () => {
    // A-8/BRULE-067. This is the ONE moment the bidder names are shown, and it has to come first:
    // reading my-evaluation opens scoring as a side effect, so a page that loaded both at once would
    // pass the window before the evaluator saw a name.
    const calls: { url: string; method: string; body: string }[] = []
    restore = mockFetch({
      '/api/v1/rfqs/RFQ-2026-000001/my-evaluation/bidders': {
        declarationRequired: true,
        bidders: [{ proposalCode: 'PRP-2026-000001', supplierDisplayNameAr: 'شركة الاختبار', supplierDisplayNameEn: 'Test Supplies Co' }],
      },
      '/api/v1/rfqs/RFQ-2026-000001/my-evaluation': myEvaluationFixture(),
    }, calls)

    renderPage(<MyEvaluationPage />)

    expect(await screen.findByText('Conflict of interest declaration')).toBeInTheDocument()
    expect(screen.getByText('Test Supplies Co')).toBeInTheDocument()

    // The workspace read must NOT have happened - that is the part that would have opened scoring.
    expect(calls.some((c) => c.url.endsWith('/my-evaluation'))).toBe(false)

    // Recusal needs a reason; continuing does not.
    expect(screen.getByRole('button', { name: 'I have a conflict — recuse me' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'No conflict — continue' })).toBeEnabled()
  })

  it('sends the recusal reason when the evaluator declares a conflict', async () => {
    const calls: { url: string; method: string; body: string }[] = []
    restore = mockFetch({
      '/api/v1/rfqs/RFQ-2026-000001/my-evaluation/bidders': {
        declarationRequired: true,
        bidders: [{ proposalCode: 'PRP-2026-000001', supplierDisplayNameAr: 'شركة الاختبار', supplierDisplayNameEn: 'Test Supplies Co' }],
      },
      '/api/v1/rfqs/RFQ-2026-000001/my-evaluation/declare': {},
      '/api/v1/rfqs/RFQ-2026-000001/my-evaluation': myEvaluationFixture(),
    }, calls)

    renderPage(<MyEvaluationPage />)

    await userEvent.type(await screen.findByLabelText('Reason for recusal'), 'A former employer.')
    await userEvent.click(screen.getByRole('button', { name: 'I have a conflict — recuse me' }))

    await vi.waitFor(() => expect(calls.some((c) => c.url.includes('/declare'))).toBe(true))
    expect(JSON.parse(calls.find((c) => c.url.includes('/declare'))!.body))
      .toEqual({ hasConflict: true, reason: 'A former employer.' })
  })
})
