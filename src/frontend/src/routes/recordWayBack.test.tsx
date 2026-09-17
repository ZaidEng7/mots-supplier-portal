// Every record screen keeps its way back to its parent on every branch it can render, not only once its data has arrived.
//
// The defect this closes. Outside the tender workspace a record screen's link to its parent was written inside the success
// return, or nowhere. The supplier's proposal had no link and no navigate call in any branch: the tender's code was subtitle
// text, a supplier with no bid yet had one control and it was a server write, and a Withdrawn proposal had no control at all,
// while the tender's attachments, clarifications and addenda are only on the tender's own page. The evaluator's brief and the
// reviewer's application each had their link, but only below the early returns, so the skeleton, the failure and "not found"
// were a sentence or a retry button with nowhere to go. Every one of those parents is addressed by the route's params alone,
// so the link was missing only because of where it was drawn. Each screen now builds it before its first early return.
//
// THE DENOMINATOR comes from the router. Every parameterised route, by reachability's own PARAMETERISED_ROUTE, is classified
// exactly once: as one of the tender views TenderFrame covers, which tenderFrameCoverage renders and whose addresses the
// harness lists; as a row in PARENT_OF below, with the address of its parent; or as an exemption with its reason typed out.
// The union must equal the router's set, so a new record route fails here until somebody decides which it is. Each parent
// must itself be a route, with the record's code in place of the parameter, and must not be the record.
//
// THE RULE renders each PARENT_OF screen in the three states every record has - a request that never answers, a server
// failure and a refusal that says the record is not there - and in the states that are its own: the proposal page with no bid
// yet and with a Withdrawn bid, and the review screen answered with no application. Each render waits for that branch's OWN
// marker before it looks for the link, because a render that never reached the branch would otherwise pass on the link of a
// different one. The brief's skeleton has no label, so its marker is the skeleton element itself.
//
// The router hooks are mocked once for the file. The address parameter is a hoisted object each case sets before rendering,
// because the proposal and the brief are addressed by a tender's code and the review by a supplier's. The link stub is the
// harness's TestLink, which resolves `to` and `params` into a real href, so a link to the right route with the wrong code fails.
//
// THE CONTROL is the matcher on both ends. A paragraph carrying the link's words and no anchor reports no link at all, a
// TestLink to the right route with another tender's code reports that other address, and the same link with the right code
// reports the parent. A matcher that found a link in everything would keep every case above green.

import { afterEach, describe, expect, it, vi } from 'vitest'
import type { ReactElement } from 'react'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import { renderPage, mockFetch } from '../test/renderPage'
import { TENDER_CODE, TENDER_FRAME_SCREENS, TestLink } from './back-office/rfq/tenderTestHarness'
import type { SupplierRfq } from '../api/supplierRfqs'
import type { Proposal } from '../api/proposals'

const routeParams = vi.hoisted(() => ({ referenceCode: '' }))

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, useParams: () => routeParams, Link: TestLink }
})

const { router } = await import('../router')
const { PARAMETERISED_ROUTE } = await import('../shells/navigation')
const i18n = (await import('../i18n/config')).default
const { SupplierProposalPage } = await import('./SupplierProposalPage')
const { MyEvaluationBriefPage } = await import('./back-office/MyEvaluationBriefPage')
const { ReviewApplicationPage } = await import('./ReviewApplicationPage')

type Marker = (container: HTMLElement) => Promise<unknown>

interface Branch {
  name: string
  routes: Record<string, unknown> | 'never answers'
  reached: Marker
}

interface RecordScreen {
  referenceCode: string
  page: () => ReactElement
  reached: { pending: Marker; failed: Marker; notFound: Marker }
  ownBranches: Branch[]
}

const SUPPLIER_CODE = 'SUP-2026-000039'

const PARENT_OF = {
  '/rfqs/$referenceCode/proposal': `/rfqs/${TENDER_CODE}`,
  '/back-office/rfqs/$referenceCode/brief': `/back-office/rfqs/${TENDER_CODE}/my-evaluation`,
  '/back-office/review/$referenceCode': '/back-office/review',
} as const

type RecordPath = keyof typeof PARENT_OF

const EXEMPT: Record<string, string> = {
  '/rfqs/$referenceCode':
    'A top-level record. The supplier opens a tender from the Tenders list, and that list is its own row in the supplier '
    + 'sidebar, highlighted while the tender is open, so the parent is on screen on every branch this page renders.',
  '/back-office/ministry/rfqs/$referenceCode':
    'A top-level record. The ministry viewer opens a tender from the tender monitor, and the monitor is its own row in the '
    + 'back-office sidebar, highlighted while the record is open, so the parent is on screen on every branch this page renders.',
}

const API_TENDER = `/api/v1/rfqs/${TENDER_CODE}`

function invitedTender(): SupplierRfq {
  return {
    rfqCode: TENDER_CODE, titleAr: 'طلب تجريبي', titleEn: 'Sample RFQ', descriptionAr: null, descriptionEn: null,
    currencyCode: 'SYP', state: 'SubmissionOpen', submissionOpensAt: null, submissionDeadline: null,
    clarificationDeadlineAt: null, items: [], requirements: [], attachments: [], invitationStatus: 'Invited',
    clarifications: [], addenda: [], submissionDeadlineChangeReason: null, submissionDeadlineChangedAt: null,
  }
}

function withdrawnProposal(): Proposal {
  return {
    proposalCode: 'PRP-2026-000001', rfqCode: TENDER_CODE, state: 'Withdrawn', currency: null, paymentTerms: null,
    incotermCode: null, deliveryTermsAr: null, deliveryTermsEn: null, warranty: null, validityStart: null, validityEnd: null,
    narrativeAr: null, narrativeEn: null, submittedAt: '2026-08-30T10:00:00Z', withdrawnAt: '2026-08-31T09:00:00Z',
    withdrawReason: 'Priced in the wrong currency', clarificationReason: null, clarificationRequestedAt: null,
    revisionNumber: 0, createdAt: '2026-08-30T09:00:00Z', totals: { currency: null, grandTotal: 0 }, validityDays: null,
    items: [], documents: [], requirementAnswers: [],
  }
}

const reachedText = (text: string): Marker => () => screen.findByText(text)
const reachedLoading: Marker = () => screen.findAllByText(i18n.t('common.loading'))
const reachedAlert: Marker = () => screen.findAllByRole('alert')
const reachedSkeleton: Marker = (container) => waitFor(() => {
  expect(container.querySelector('.msp-skeleton')).not.toBeNull()
})

const RECORDS: Record<RecordPath, RecordScreen> = {
  '/rfqs/$referenceCode/proposal': {
    referenceCode: TENDER_CODE,
    page: () => <SupplierProposalPage />,
    reached: {
      pending: reachedLoading,
      failed: () => screen.findByText(i18n.t('supplierRfq.notFound')),
      notFound: () => screen.findByText(i18n.t('supplierRfq.notFound')),
    },
    ownBranches: [
      {
        name: 'no bid yet',
        routes: { '/api/v1/': { __status: 404 }, [API_TENDER]: invitedTender(), [`${API_TENDER}/proposals`]: { __status: 404 } },
        reached: () => screen.findByRole('button', { name: i18n.t('proposal.start') }),
      },
      {
        name: 'withdrawn',
        routes: { '/api/v1/': { __status: 404 }, [API_TENDER]: invitedTender(), [`${API_TENDER}/proposals`]: withdrawnProposal() },
        reached: () => screen.findByText(i18n.t('status.proposal.Withdrawn')),
      },
    ],
  },
  '/back-office/rfqs/$referenceCode/brief': {
    referenceCode: TENDER_CODE,
    page: () => <MyEvaluationBriefPage referenceCode={TENDER_CODE} />,
    reached: {
      pending: reachedSkeleton,
      failed: () => screen.findByText(i18n.t('evaluationBrief.error')),
      notFound: () => screen.findByText(i18n.t('evaluationBrief.error')),
    },
    ownBranches: [],
  },
  '/back-office/review/$referenceCode': {
    referenceCode: SUPPLIER_CODE,
    page: () => <ReviewApplicationPage />,
    reached: {
      pending: reachedText('...'),
      failed: reachedAlert,
      notFound: reachedAlert,
    },
    ownBranches: [
      {
        name: 'answered with no application',
        routes: { [`/api/v1/review/${SUPPLIER_CODE}`]: null },
        reached: () => screen.findByText(i18n.t('errors.notFound')),
      },
    ],
  },
}

const EVERY_RECORD_HAS: { name: string; routes: Branch['routes']; marker: keyof RecordScreen['reached'] }[] = [
  { name: 'pending', routes: 'never answers', marker: 'pending' },
  { name: 'failed', routes: { '/api/v1/': { __status: 500 } }, marker: 'failed' },
  { name: 'not found', routes: { '/api/v1/': { __status: 404 } }, marker: 'notFound' },
]

const CASES = (Object.entries(RECORDS) as [RecordPath, RecordScreen][]).flatMap(([path, record]) => [
  ...EVERY_RECORD_HAS.map((state) => ({ path, record, branch: { name: state.name, routes: state.routes, reached: record.reached[state.marker] } })),
  ...record.ownBranches.map((branch) => ({ path, record, branch })),
]).map((testCase) => ({ ...testCase, state: testCase.branch.name }))

function linksIn(container: HTMLElement): string[] {
  return within(container).queryAllByRole('link').map((link) => link.getAttribute('href') ?? '')
}

function declaredRoutes(): string[] {
  const byId = router.routesById as unknown as Record<string, { fullPath?: string }>
  return [...new Set(
    Object.values(byId)
      .map((route) => route.fullPath)
      .filter((path): path is string => typeof path === 'string' && path.length > 0)
      .map((path) => (path.length > 1 ? path.replace(/\/$/, '') : path)),
  )].sort()
}

function neverAnswer(): () => void {
  const original = globalThis.fetch
  globalThis.fetch = (() => new Promise<Response>(() => undefined)) as typeof fetch
  return () => { globalThis.fetch = original }
}

describe('every record screen keeps its way back to its parent', () => {
  let restore: (() => void) | undefined

  afterEach(() => {
    cleanup()
    restore?.()
    restore = undefined
    routeParams.referenceCode = ''
  })

  it('classifies every parameterised route exactly once', () => {
    const parameterised = declaredRoutes().filter((path) => PARAMETERISED_ROUTE.test(path))

    expect(parameterised.length).toBeGreaterThan(10)
    expect(parameterised).toContain('/rfqs/$referenceCode/proposal')
    expect(TENDER_FRAME_SCREENS).toHaveLength(7)
    expect(Object.keys(PARENT_OF)).toHaveLength(3)
    expect(Object.keys(EXEMPT)).toHaveLength(2)

    const classified = [...TENDER_FRAME_SCREENS, ...Object.keys(PARENT_OF), ...Object.keys(EXEMPT)]
    const twice = classified.filter((path, index) => classified.indexOf(path) !== index)
    expect(twice, 'a route classified twice has two answers to where its way back is').toEqual([])
    expect(
      [...classified].sort(),
      'a parameterised route nobody classified is a record screen nobody checked for a way back',
    ).toEqual(parameterised)

    for (const [path, reason] of Object.entries(EXEMPT)) {
      expect(reason.length, `${path}'s exemption must say why`).toBeGreaterThan(80)
    }
    expect(Object.keys(RECORDS).sort()).toEqual(Object.keys(PARENT_OF).sort())
    expect(CASES.length).toBe(3 * EVERY_RECORD_HAS.length + 3)
  })

  it('declares a parent that is a real route and is not the record itself', () => {
    const routes = declaredRoutes()
    for (const [path, parent] of Object.entries(PARENT_OF) as [RecordPath, string][]) {
      const parentRoute = parent.replace(RECORDS[path].referenceCode, '$referenceCode')
      expect(routes, `${path}'s parent ${parent} is not a route`).toContain(parentRoute)
      expect(parentRoute).not.toBe(path)
    }
  })

  it.each(CASES)('$path, $state', async ({ path, record, branch }) => {
    routeParams.referenceCode = record.referenceCode
    restore = branch.routes === 'never answers' ? neverAnswer() : mockFetch(branch.routes)

    const { container } = renderPage(record.page())

    await branch.reached(container)

    expect(
      linksIn(container),
      `${path} in its ${branch.name} state renders no link to its parent ${PARENT_OF[path]}`,
    ).toContain(PARENT_OF[path])
  })

  it('the matcher reports no link for the words alone, and the wrong address for the wrong code', () => {
    const parent = PARENT_OF['/rfqs/$referenceCode/proposal']
    const words = i18n.t('proposal.backToTender')

    const bare = renderPage(<p>{words}</p>)
    expect(screen.getByText(words)).toBeInTheDocument()
    expect(linksIn(bare.container)).toEqual([])
    bare.unmount()

    const wrong = renderPage(<TestLink to="/rfqs/$referenceCode" params={{ referenceCode: 'RFQ-2026-000002' }}>{words}</TestLink>)
    expect(linksIn(wrong.container)).toEqual(['/rfqs/RFQ-2026-000002'])
    expect(linksIn(wrong.container)).not.toContain(parent)
    wrong.unmount()

    const right = renderPage(<TestLink to="/rfqs/$referenceCode" params={{ referenceCode: TENDER_CODE }}>{words}</TestLink>)
    expect(linksIn(right.container)).toEqual([parent])
  })
})
