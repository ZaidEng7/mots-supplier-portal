// FEAT-11.3 and FR-EVL-003 through 005: the two-envelope gate's frontend half - a UI convenience only, hide and disable, never
// the real gate, which is ScoreCriterionHandler and is proven server-side in the backend integration suite. This file proves
// the financial input is disabled pre-qualification and becomes enabled once the fixture reports the proposal qualified.
//
// THE FIXTURES. Link is mocked to a plain anchor as well as the param, because the header links to SCR-501's brief and this
// harness has no router - the same treatment every other page test that renders a link uses. The default evaluator has
// already declared, so the window is shut and the workspace is what renders. And the default bid models what the SERVER sends
// while scoring is open: the pseudonym, and NULL for every identity field - a fixture carrying a name there would be a shape
// the API cannot produce, and the test would prove nothing about the screen's real input.
//
// Several tests declare the declaration state EXPLICITLY. Without it the longest-match in mockFetch answers the bidders read
// with the my-evaluation fixture, whose declarationRequired is undefined, and the page then works for an accidental reason -
// which is not a test.
//
// Not assigned shows the not-assigned message instead of a crash. The financial score input is disabled until the proposal is
// technically qualified, and enabled once it is, with a save that toasts.
//
// BRULE-061: a criterion requiring justification cannot be saved until one is written. The defect this closes: the flag was on
// the wire since EPIC-07 and the scoring form sent commentAr and commentEn as null unconditionally, so scoring such a
// criterion answered a domain refusal the evaluator had no field to satisfy. The rule is enforced server-side either way -
// what is asserted here is that the screen states it before the score is thrown away. The justification is sent in ONE
// language, the evaluator's own: BRULE-061 accepts either and requires no translation, and this harness runs in English, so
// commentEn carries the text and commentAr stays null. That is asserted on the REQUEST rather than on the screen, because
// which field the words land in is the part a later refactor could silently change.
//
// A submitted evaluation shows the already-submitted message instead of a submit button.
//
// T-067: the bid content the evaluator is scoring renders, which was absent entirely before - the page printed a proposal
// GUID as the bid's identity and nothing else. The bid is identified by its PSEUDONYM while scoring is open rather than by its
// owner, and the specification is on the same screen as the scoring.
//
// A-8, superseding D-19: the bidder identity is WITHHELD while scoring is open, and the evaluator is told so plainly rather
// than left to wonder whether the data is missing. The control is that the bidder is NAMED once the identity is revealed -
// the server decides when, before scoring opens for BRULE-067's recusal declaration and after consolidation when the scores
// are locked, and the screen renders whichever it is given. Both labels show, so a comment written under the pseudonym still
// reads.
//
// THE DECLARATION comes first, and the page asks for it before it will load the workspace. A-8 and BRULE-067: this is the ONE
// moment the bidder names are shown, and it has to come first, because reading my-evaluation opens scoring as a side effect
// and a page that loaded both at once would pass the window before the evaluator saw a name. The workspace read must NOT have
// happened - that is the part that would have opened scoring. Recusal needs a reason and continuing does not, and the last
// test sends that reason.

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch, type RecordedRequest } from '../../test/renderPage'
import type { MyEvaluation } from '../../api/evaluations'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, useParams: () => ({ referenceCode: 'RFQ-2026-000001' }), useRouterState: () => '/back-office/rfqs/RFQ-2026-000001', Link: 'a' }
})

const { MyEvaluationPage } = await import('./MyEvaluationPage')

const DECLARED = { declarationRequired: false, bidders: [] }

function myEvaluationFixture(overrides: Partial<MyEvaluation> = {}): MyEvaluation {
  return {
    rfqReferenceCode: 'RFQ-2026-000001', state: 'InProgress', submittedAt: null,
    rfqTitleAr: 'طلب تجريبي', rfqTitleEn: 'Catering RFQ',
    rfqDescriptionAr: null, rfqDescriptionEn: null,
    rfqItems: [], rfqRequirements: [],
    criteria: [
      { id: 'crit-tech', nameAr: 'جودة', nameEn: 'Quality', dimension: 'Technical', weight: 60, maxScore: 100, threshold: 60, scoringType: 'Numeric', isFinancial: false },
      { id: 'crit-fin', nameAr: 'سعر', nameEn: 'Price', dimension: 'Commercial', weight: 40, maxScore: 100, threshold: null, scoringType: 'Numeric', isFinancial: true },
    ],
    proposals: [{
      proposalCode: 'PRP-2026-000001',
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

  it('BRULE-061: a criterion requiring justification cannot be saved until one is written', async () => {
    restore = mockFetch({
      '/api/v1/rfqs/RFQ-2026-000001/my-evaluation/bidders': DECLARED,
      '/api/v1/rfqs/RFQ-2026-000001/my-evaluation': myEvaluationFixture({
        criteria: [
          { id: 'crit-tech', nameAr: 'جودة', nameEn: 'Quality', dimension: 'Technical', weight: 100, maxScore: 100, threshold: 60, scoringType: 'Numeric', isFinancial: false, requiresJustification: true },
        ],
      }),
    })

    renderPage(<MyEvaluationPage />)

    await userEvent.type(await screen.findByLabelText('Score: Quality'), '75')
    expect(screen.getByRole('button', { name: 'Save score' })).toBeDisabled()
    expect(screen.getByText('This criterion requires a justification before the score can be saved.')).toBeInTheDocument()

    await userEvent.type(screen.getByLabelText('Justification: Quality'), 'Met every stated requirement.')
    expect(screen.getByRole('button', { name: 'Save score' })).toBeEnabled()
  })

  it('sends the justification in ONE language - the evaluator\'s own', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch({
      '/api/v1/rfqs/RFQ-2026-000001/my-evaluation/bidders': DECLARED,
      '/api/v1/rfqs/RFQ-2026-000001/my-evaluation': myEvaluationFixture({
        criteria: [
          { id: 'crit-tech', nameAr: 'جودة', nameEn: 'Quality', dimension: 'Technical', weight: 100, maxScore: 100, threshold: 60, scoringType: 'Numeric', isFinancial: false, requiresJustification: true },
        ],
      }),
    }, recorded)

    renderPage(<MyEvaluationPage />)

    await userEvent.type(await screen.findByLabelText('Score: Quality'), '75')
    await userEvent.type(screen.getByLabelText('Justification: Quality'), 'Cheapest compliant bid.')
    await userEvent.click(screen.getByRole('button', { name: 'Save score' }))

    expect(await screen.findByText('Score saved')).toBeInTheDocument()
    const scored = recorded.find((request) => request.method === 'POST' && request.url.includes('/scores'))
    expect(scored).toBeDefined()
    expect(JSON.parse(scored!.body)).toMatchObject({ commentEn: 'Cheapest compliant bid.', commentAr: null })
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
    restore = mockFetch({
      '/api/v1/rfqs/RFQ-2026-000001/my-evaluation/bidders': DECLARED,
      '/api/v1/rfqs/RFQ-2026-000001/my-evaluation': myEvaluationFixture(),
    })

    renderPage(<MyEvaluationPage />)

    expect(await screen.findByText('We use 300-thread cotton.')).toBeInTheDocument()
    expect(screen.getByText('Bidder A')).toBeInTheDocument()
    expect(screen.getByText(/PRP-2026-000001/)).toBeInTheDocument()
    expect(screen.getByText(/Catering RFQ/)).toBeInTheDocument()
  })

  it('withholds the bidder identity while scoring is open', async () => {
    restore = mockFetch({
      '/api/v1/rfqs/RFQ-2026-000001/my-evaluation/bidders': DECLARED,
      '/api/v1/rfqs/RFQ-2026-000001/my-evaluation': myEvaluationFixture(),
    })

    renderPage(<MyEvaluationPage />)

    expect(await screen.findByText('Bidder A')).toBeInTheDocument()
    expect(screen.getByText('Bidder identity withheld during scoring')).toBeInTheDocument()
    expect(screen.queryByText(/Test Supplies Co/)).not.toBeInTheDocument()
  })

  it('names the bidder once the identity is revealed', async () => {
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

    expect(calls.some((c) => c.url.endsWith('/my-evaluation'))).toBe(false)

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
