// Every view of a tender keeps its way back to the tender, on every branch it can render - not only once its data has arrived.
//
// The defect this closes. The tender's head and tab strip were drawn inside each view's success return, so every earlier return
// drew neither. A comparison with no submitted bid - every tender before its window closes - showed one sentence. A tender that
// failed to load on its own tab, its suppliers, its award or its settings showed a retry button. An evaluator opening an
// assignment for the first time met the conflict-of-interest declaration, whose only controls cannot be taken back, and an
// evaluator who had just recused themselves was told "not assigned"; neither screen had a single link. The frame needed no data
// from the page, so it was missing only because of where it was drawn, and TenderFrame now draws it around every return.
//
// THE DENOMINATOR comes from the router rather than from a listing of the folder: every route under the tender's own path is
// either a screen in the table below or an exemption with its reason typed out by hand, and the table is pinned at seven. A new
// view of a tender fails here until somebody decides which it is. The one exemption is the brief, which is not a tab, keeps its
// own page title, and belongs to the record back-link work rather than to this frame. The table's addresses must equal the
// harness's TENDER_FRAME_SCREENS, because recordWayBack reads that list to know which record routes this file already covers.
//
// THE RULE renders each screen in the states every screen has - a request that never answers, a server failure and a refusal -
// and in the states that are its own: the comparison with no bids and with no comparison at all, and the evaluator's
// declaration, "not assigned" and a workspace that failed after declaring. Each render waits for that branch's OWN marker (the
// skeleton's label, the alert, or the branch's copy) before it asks about the strip, because a render that never reached the
// branch would otherwise pass on the frame of a different one. The officer's screens are rendered with an officer's claims and
// the evaluator's with an evaluator's. Each render is unmounted BEFORE the claims are cleared, because the tender's own view
// reads the claims and would otherwise re-render after its test had finished.
//
// The way back is a link in the tender's section strip to the tender. For the evaluator it is their own view of it instead: the
// strip offers each tab only to a reader who can open it, and a pure evaluator cannot open the tender, so their strip is the My
// evaluation tab alone. Either destination counts for the evaluator, so a reader who both scores and reads the tender passes too.
//
// The declaration also asserts that nothing on the gate read the evaluator's workspace. That read opens scoring, the declaration
// window is held only by the page not issuing it, and the frame is new code on that screen.
//
// THE CONTROL is the same matcher on both ends: a bare failure panel has no way back, and the same panel inside the frame, for
// a signed-in officer, has one. The officer is signed in because the strip offers a tab only to a reader who can open it, so a
// frame with no session is offered nothing. A matcher that answered true for everything would keep every case above green.

import { afterEach, describe, expect, it, vi } from 'vitest'
import type { ComponentType } from 'react'
import { cleanup, screen, within } from '@testing-library/react'
import { renderPage, mockFetch, type RecordedRequest } from '../../../test/renderPage'
import { TENDER_CODE, TENDER_FRAME_SCREENS, TENDER_ROUTES, TestLink, rfqFixture } from './tenderTestHarness'
import type { Comparison } from '../../../api/comparison'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return {
    ...actual,
    useParams: () => ({ referenceCode: TENDER_CODE }),
    useRouterState: () => `/back-office/rfqs/${TENDER_CODE}`,
    Link: TestLink,
  }
})

const { router } = await import('../../../router')
const { useAuthStore } = await import('../../../lib/authStore')
const i18n = (await import('../../../i18n/config')).default
const { QueryError } = await import('../../../components/ui')
const { TenderFrame } = await import('./TenderFrame')
const { RfqDetailPage } = await import('../RfqDetailPage')
const { TenderSuppliersPage } = await import('../TenderSuppliersPage')
const { ReceivedProposalsPage } = await import('../ReceivedProposalsPage')
const { ComparisonPage } = await import('../ComparisonPage')
const { AwardPage } = await import('../AwardPage')
const { TenderSettingsPage } = await import('../TenderSettingsPage')
const { MyEvaluationPage } = await import('../MyEvaluationPage')

type Audience = 'officer' | 'evaluator'

interface State {
  name: string
  routes: Record<string, unknown> | 'never answers'
  reached: () => Promise<unknown>
  mustNotRequest?: RegExp
}

interface Screen {
  component: ComponentType
  audience: Audience
  ownStates: State[]
}

const TENDER_PATH = '/back-office/rfqs/$referenceCode'
const TENDER_HREF = `/back-office/rfqs/${TENDER_CODE}`
const API = `/api/v1/rfqs/${TENDER_CODE}`

const WAY_BACK: Record<Audience, readonly string[]> = {
  officer: [TENDER_HREF],
  evaluator: [TENDER_HREF, `${TENDER_HREF}/my-evaluation`],
}

const PERMISSIONS: Record<Audience, string[]> = {
  officer: [
    'rfq.publish', 'offering.search', 'supplier.directory.read', 'rfq.read', 'rfq.create', 'rfq.edit', 'rfq.submit_review',
    'rfq.close', 'rfq.invite', 'clarification.answer', 'rfq.clarify', 'rfq.addendum', 'evaluation.open',
    'evaluation.consolidate', 'comparison.view', 'award.recommend',
  ],
  evaluator: ['evaluation.score', 'evaluation.submit', 'rfq.clarify'],
}

const reachedLoading = () => screen.findAllByText(i18n.t('common.loading'))
const reachedAlert = () => screen.findAllByRole('alert')
const reachedText = (key: string) => () => screen.findByText(i18n.t(key))

const EVERY_SCREEN_HAS: State[] = [
  { name: 'pending', routes: 'never answers', reached: reachedLoading },
  { name: 'failed', routes: { '/api/v1/': { __status: 500 } }, reached: reachedAlert },
  { name: 'forbidden', routes: { '/api/v1/': { __status: 403 } }, reached: reachedAlert },
]

const OFFICER_TENDER = { ...TENDER_ROUTES, [API]: rfqFixture('Published') }
const EVALUATOR_REFUSED = { '/api/v1/': { __status: 403 } }
const DECLARED = { declarationRequired: false, bidders: [] }

function noBids(): Comparison {
  return {
    rfqReferenceCode: TENDER_CODE, rfqTitleAr: 'طلب تجريبي', rfqTitleEn: 'Sample RFQ', evaluationState: 'NotStarted',
    rfqItems: [], proposals: [],
  }
}

const SCREENS: Record<string, Screen> = {
  [TENDER_PATH]: { component: RfqDetailPage, audience: 'officer', ownStates: [] },
  [`${TENDER_PATH}/suppliers`]: { component: TenderSuppliersPage, audience: 'officer', ownStates: [] },
  [`${TENDER_PATH}/proposals`]: { component: ReceivedProposalsPage, audience: 'officer', ownStates: [] },
  [`${TENDER_PATH}/comparison`]: {
    component: ComparisonPage,
    audience: 'officer',
    ownStates: [
      { name: 'no bids', routes: { ...OFFICER_TENDER, [`${API}/comparison`]: noBids() }, reached: reachedText('comparison.empty') },
      { name: 'not found', routes: { ...OFFICER_TENDER, [`${API}/comparison`]: { __status: 404 } }, reached: reachedText('comparison.notFound') },
    ],
  },
  [`${TENDER_PATH}/award`]: { component: AwardPage, audience: 'officer', ownStates: [] },
  [`${TENDER_PATH}/settings`]: { component: TenderSettingsPage, audience: 'officer', ownStates: [] },
  [`${TENDER_PATH}/my-evaluation`]: {
    component: MyEvaluationPage,
    audience: 'evaluator',
    ownStates: [
      {
        name: 'declaration required',
        routes: {
          ...EVALUATOR_REFUSED,
          [`${API}/my-evaluation/bidders`]: {
            declarationRequired: true,
            bidders: [{ proposalCode: 'PRP-2026-000001', supplierDisplayNameAr: 'مورد', supplierDisplayNameEn: 'A Supplier' }],
          },
        },
        reached: reachedText('evaluation.my.declaration.title'),
        mustNotRequest: /\/my-evaluation(\?|$)/,
      },
      {
        name: 'not assigned',
        routes: { ...EVALUATOR_REFUSED, [`${API}/my-evaluation/bidders`]: DECLARED, [`${API}/my-evaluation`]: null },
        reached: reachedText('evaluation.my.notAssigned'),
      },
      {
        name: 'workspace failed after declaring',
        routes: { ...EVALUATOR_REFUSED, [`${API}/my-evaluation/bidders`]: DECLARED, [`${API}/my-evaluation`]: { __status: 500 } },
        reached: reachedAlert,
      },
    ],
  },
}

const EXEMPT: Record<string, string> = {
  [`${TENDER_PATH}/brief`]:
    'Not one of the tender\'s tabs. The brief is the evaluator\'s reading of the scoring guidance, keeps its own page title, '
    + 'and its way back is its own link to scoring, which the record back-link work covers. Drawing this frame around it would '
    + 'put a second title on the screen.',
}

function hasWayBackToTender(container: HTMLElement, destinations: readonly string[]): boolean {
  return within(container)
    .queryAllByRole('navigation', { name: i18n.t('rfq.tabs.label') })
    .some((nav) => within(nav).queryAllByRole('link').some((link) => destinations.includes(link.getAttribute('href') ?? '')))
}

function neverAnswer(): () => void {
  const original = globalThis.fetch
  globalThis.fetch = (() => new Promise<Response>(() => undefined)) as typeof fetch
  return () => { globalThis.fetch = original }
}

function signIn(audience: Audience) {
  useAuthStore.setState({
    accessToken: 'token',
    status: 'authenticated',
    claims: { userId: `u-${audience}`, email: `${audience}@example.test`, organizationId: 'org-1', permissions: PERMISSIONS[audience] },
  })
}

const CASES = Object.entries(SCREENS).flatMap(([path, definition]) =>
  [...EVERY_SCREEN_HAS, ...definition.ownStates].map((state) => ({ path, state: state.name, definition, definedState: state })),
)

describe('every view of a tender keeps its way back to the tender', () => {
  let restore: (() => void) | undefined

  afterEach(() => {
    cleanup()
    restore?.()
    restore = undefined
    useAuthStore.setState({ accessToken: null, claims: null, status: 'idle', expired: false, lastEmail: null })
  })

  it('names every route under the tender, as a screen or as an exemption with its reason', () => {
    const routes = Object.values(router.routesById as Record<string, { fullPath: string }>)
      .map((route) => route.fullPath)
      .filter((fullPath) => fullPath.startsWith(TENDER_PATH))
      .sort()

    expect(Object.keys(SCREENS)).toHaveLength(7)
    expect(Object.keys(SCREENS).sort()).toEqual([...TENDER_FRAME_SCREENS].sort())
    expect(routes).toEqual([...Object.keys(SCREENS), ...Object.keys(EXEMPT)].sort())
    for (const [path, reason] of Object.entries(EXEMPT)) {
      expect(reason.length, `${path}'s exemption must say why`).toBeGreaterThan(80)
    }
    expect(CASES.length).toBe(7 * EVERY_SCREEN_HAS.length + 5)
  })

  it.each(CASES)('$path, $state', async ({ path, state, definition, definedState }) => {
    signIn(definition.audience)
    const recorded: RecordedRequest[] = []
    restore = definedState.routes === 'never answers' ? neverAnswer() : mockFetch(definedState.routes, recorded)

    const Page = definition.component
    const { container } = renderPage(<Page />)

    await definedState.reached()

    expect(
      hasWayBackToTender(container, WAY_BACK[definition.audience]),
      `${path} in its ${state} state renders no link back to the tender in the tender's section strip`,
    ).toBe(true)

    if (definedState.mustNotRequest) {
      const pattern = definedState.mustNotRequest
      expect(recorded.filter((request) => pattern.test(request.url)).map((request) => request.url)).toEqual([])
    }
  })

  it('the matcher can fail, and can pass', async () => {
    const bare = renderPage(<QueryError error={new Error('unavailable')} onRetry={() => undefined} />)
    await screen.findByRole('alert')
    expect(hasWayBackToTender(bare.container, WAY_BACK.officer)).toBe(false)
    expect(hasWayBackToTender(bare.container, WAY_BACK.evaluator)).toBe(false)
    bare.unmount()

    restore = mockFetch({ '/api/v1/': { __status: 500 } })
    signIn('officer')
    const framed = renderPage(
      <TenderFrame referenceCode={TENDER_CODE}>
        <QueryError error={new Error('unavailable')} onRetry={() => undefined} />
      </TenderFrame>,
    )
    await screen.findByRole('alert')
    expect(hasWayBackToTender(framed.container, WAY_BACK.officer)).toBe(true)
    expect(hasWayBackToTender(framed.container, ['/back-office/rfqs/SOME-OTHER-TENDER'])).toBe(false)
  })
})
