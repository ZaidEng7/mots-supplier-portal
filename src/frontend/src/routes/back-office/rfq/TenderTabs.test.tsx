// The views of one tender, and who is offered each of them.
//
// What this replaces is a page that stacked every section down one column and reached the bids, the comparison and the award
// through buttons two thirds of the way down it. The strip has to be right about three things: where each tab goes, which one
// you are on, and whether you could open it at all.
//
// These use renderPage rather than a bare render, because it is what initialises i18n and these assertions are about the
// words a reader sees on the tabs. The fixture answers the two reads the strip makes for itself - seven invited and four
// bids - and every test signs in with every permission unless it is asking about a persona, so the tabs about routes and
// counts see the whole strip. Each render is unmounted BEFORE the session is cleared, because the strip reads the session and
// would otherwise re-render after its test had finished.
//
// Each tab goes to its own route, carrying the reference code, and exactly one is marked as the page you are on. The
// denominator for that is the reason the tender tab is matched exactly rather than by prefix: every other tab's path begins
// with the tender's own, so a prefix match would light up "Tender" from inside Settings - the same defect the supplier
// navigation had against its dashboard link, where every page in the product claimed to be the dashboard.
//
// THE COUNTS are why these are tabs rather than a menu: "Suppliers" and "Suppliers 7" ask a reader for different amounts of
// work, and the second answers a question they would otherwise open the tab to ask. Only the two tabs that have a number
// get one - a count of nothing on Award would be an invitation to wonder what it counted. That test waits for one of the
// strip's own two queries, and reads the labels as TEXT rather than by accessible name, because the name is what it asserts:
// a count glued to its label is what a screen reader would say without the separator between them.
//
// The denominator for the counts is the test after it. A strip that printed 0 when the request failed would state a fact -
// that nobody has bid - because it could not read one. No number is the honest answer to not knowing, and the strip is
// navigation first: every destination is still there, which is the half that must not break.
//
// WHO IS OFFERED WHAT. The strip gave every persona the same six tabs. An evaluator holds evaluation.score, evaluation.submit
// and rfq.clarify, and every one of those six answered 403 for them - none of them was the scoring screen they were standing
// on, so nothing was marked current and nothing led back to it. TAB_REQUIRES is written by hand, one row per view, naming the
// permissions the view needs next to the endpoint that enforces them, so a reader can check each row against the API rather
// than against the component. A view is keyed by its path below the tender, and the tender's own view as "tender".
//
// The DENOMINATOR comes first: signed in with every permission there is, the strip renders exactly as many links as the map
// has rows, and every link is a row in the map - so a new tab with no declared requirement fails here before any persona is
// asked about. THE RULE renders the strip for the evaluator, the procurement officer and the procurement manager, and each
// must be offered exactly the views whose requirements its permissions cover. For the evaluator that is their own scoring
// alone, and on that screen it is the one tab marked current. EVERY_PERMISSION is Permissions.All and the three persona lists
// are Roles.DefaultPermissions, both copied from src/backend/Domain/Identity/Permissions.cs.
//
// THE CONTROL is the filter at both ends: a session with no permissions is offered no tab at all, and the session with every
// permission is offered all seven - a filter that hid everything would pass the evaluator's case by hiding their tab too, and
// one that hid nothing is the defect.
//
// The two count reads need rfq.read, so the strip does not issue them for a reader without it: for an evaluator they could
// only answer 403, twice, on every view. The officer beside it is the control that the same wait does see the requests.

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, within } from '@testing-library/react'
import { renderPage, mockFetch, type RecordedRequest } from '../../../test/renderPage'
import { useAuthStore } from '../../../lib/authStore'

let pathname = '/back-office/rfqs/RFQ-2026-000001'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return {
    ...actual,
    useRouterState: () => pathname,
    Link: ({ to, params, children, ...rest }: { to: string; params?: Record<string, string>; children: React.ReactNode }) => {
      const href = Object.entries(params ?? {}).reduce((path, [key, value]) => path.replace(`$${key}`, value), to)
      return <a href={href} {...rest}>{children}</a>
    },
  }
})

const { TenderTabs } = await import('./TenderTabs')

const TENDER = '/back-office/rfqs/RFQ-2026-000001'

const ROUTES = {
  '/api/v1/rfqs/RFQ-2026-000001/workspace': {
    rfqReferenceCode: 'RFQ-2026-000001', rfqState: 'SubmissionOpen', isCancelled: false,
    submittedProposalCount: 4, evaluationState: null, awardState: null, stages: [], nextActions: [],
  },
  '/api/v1/rfqs/RFQ-2026-000001': {
    referenceCode: 'RFQ-2026-000001', titleAr: 'ط', titleEn: 'T', state: 'SubmissionOpen',
    items: [], requirements: [], attachments: [], approvals: [], clarifications: [], addenda: [],
    invitations: Array.from({ length: 7 }, (_, i) => ({ id: `i-${i}`, supplierId: `s-${i}` })),
  },
}

const EVERY_PERMISSION = [
  'supplier.edit', 'supplier.submit', 'supplier.approve', 'supplier.review', 'supplier.reject', 'supplier.requestInfo',
  'supplier.document.review', 'supplier.bankAccount.manage', 'supplier.user.manage', 'supplier.lifecycle.manage',
  'rfq.publish', 'proposal.submit', 'evaluation.score', 'award.approve', 'admin.users.manage', 'audit.read',
  'admin.organizations.manage', 'admin.roles.manage', 'offering.search', 'evaluation.template.manage',
  'rfq.read', 'rfq.create', 'rfq.edit', 'rfq.submit_review', 'rfq.review', 'rfq.approve', 'rfq.close', 'rfq.cancel', 'rfq.invite',
  'clarification.answer', 'rfq.clarify', 'rfq.addendum', 'proposal.create', 'proposal.edit', 'proposal.withdraw',
  'evaluation.open', 'evaluation.assign', 'evaluation.submit', 'evaluation.consolidate', 'evaluation.finalize', 'evaluation.reopen',
  'comparison.view', 'award.reject', 'award.recommend', 'integration.retry', 'report.read', 'proposal.revise', 'proposal.decline',
  'rfq.deadline.shorten', 'reference.manage', 'governance.read', 'rfq.reassign', 'supplier.directory.read',
]

const PERSONAS: Record<'evaluator' | 'procurement_officer' | 'procurement_manager', string[]> = {
  procurement_officer: [
    'rfq.publish', 'offering.search', 'supplier.directory.read', 'rfq.read', 'rfq.create', 'rfq.edit', 'rfq.submit_review',
    'rfq.close', 'rfq.invite', 'clarification.answer', 'rfq.clarify', 'rfq.addendum', 'evaluation.open',
    'evaluation.consolidate', 'comparison.view', 'award.recommend',
  ],
  procurement_manager: [
    'report.read', 'rfq.read', 'rfq.publish', 'award.approve', 'supplier.lifecycle.manage', 'offering.search',
    'supplier.directory.read', 'rfq.review', 'rfq.approve', 'rfq.cancel', 'evaluation.template.manage', 'evaluation.open',
    'evaluation.assign', 'evaluation.consolidate', 'evaluation.finalize', 'evaluation.reopen', 'comparison.view',
    'award.recommend', 'award.reject', 'rfq.deadline.shorten', 'rfq.reassign',
  ],
  evaluator: ['evaluation.score', 'evaluation.submit', 'rfq.clarify'],
}

const TAB_REQUIRES: Readonly<Record<string, { permissions: readonly string[]; enforcedBy: string }>> = {
  tender: { permissions: ['rfq.read'], enforcedBy: 'GetRfq' },
  suppliers: { permissions: ['rfq.read'], enforcedBy: 'GetRfq' },
  proposals: { permissions: ['comparison.view'], enforcedBy: 'ListReceivedProposals' },
  comparison: { permissions: ['comparison.view'], enforcedBy: 'GetComparison' },
  'my-evaluation': { permissions: ['evaluation.score'], enforcedBy: 'GetMyEvaluation' },
  award: { permissions: ['award.recommend', 'evaluation.open'], enforcedBy: 'GetAward + GetEvaluation' },
  settings: { permissions: ['rfq.read'], enforcedBy: 'GetRfq' },
}

function renderTabs() {
  return renderPage(<TenderTabs referenceCode="RFQ-2026-000001" />)
}

function signInWith(permissions: readonly string[]) {
  useAuthStore.setState({
    accessToken: 'token',
    status: 'authenticated',
    claims: { userId: 'u-1', email: 'staff@example.test', organizationId: 'org-1', permissions: [...permissions] },
  })
}

function viewOf(href: string | null): string {
  if (href === TENDER) return 'tender'
  return href?.startsWith(`${TENDER}/`) ? href.slice(TENDER.length + 1) : String(href)
}

function offeredViews(): string[] {
  return screen.queryAllByRole('link').map((link) => viewOf(link.getAttribute('href')))
}

function viewsCoveredBy(permissions: readonly string[]): string[] {
  return Object.entries(TAB_REQUIRES)
    .filter(([, requirement]) => requirement.permissions.every((permission) => permissions.includes(permission)))
    .map(([view]) => view)
}

const settle = () => new Promise((resolve) => setTimeout(resolve, 0))

describe('TenderTabs', () => {
  let restore: (() => void) | undefined

  beforeEach(() => {
    restore = mockFetch(ROUTES)
    signInWith(EVERY_PERMISSION)
  })

  afterEach(() => {
    cleanup()
    restore?.(); restore = undefined
    pathname = TENDER
    useAuthStore.setState({ accessToken: null, claims: null, status: 'idle', expired: false, lastEmail: null })
  })

  it('sends each tab to its own route, carrying the reference code', () => {
    renderTabs()
    const nav = screen.getByRole('navigation', { name: 'Tender sections' })

    const hrefs = within(nav).getAllByRole('link').map((a) => a.getAttribute('href'))
    expect(hrefs).toEqual([
      '/back-office/rfqs/RFQ-2026-000001',
      '/back-office/rfqs/RFQ-2026-000001/suppliers',
      '/back-office/rfqs/RFQ-2026-000001/proposals',
      '/back-office/rfqs/RFQ-2026-000001/comparison',
      '/back-office/rfqs/RFQ-2026-000001/my-evaluation',
      '/back-office/rfqs/RFQ-2026-000001/award',
      '/back-office/rfqs/RFQ-2026-000001/settings',
    ])
  })

  it('marks exactly one tab as the page you are on', () => {
    pathname = '/back-office/rfqs/RFQ-2026-000001/proposals'
    renderTabs()

    const current = screen.getAllByRole('link').filter((a) => a.getAttribute('aria-current') === 'page')
    expect(current).toHaveLength(1)
    expect(current[0]).toHaveTextContent('Bids')
  })

  it('does not mark the tender tab as current from inside another tab', () => {
    pathname = '/back-office/rfqs/RFQ-2026-000001/settings'
    renderTabs()

    const tender = screen.getByRole('link', { name: 'Tender' })
    expect(tender).not.toHaveAttribute('aria-current')

    const current = screen.getAllByRole('link').filter((a) => a.getAttribute('aria-current') === 'page')
    expect(current).toHaveLength(1)
    expect(current[0]).toHaveTextContent('Settings')
  })

  it('carries a count on the two tabs that have one, and on no others', async () => {
    renderTabs()

    await screen.findByText('7')

    const labelled = Object.fromEntries(
      screen.getAllByRole('link').map((a) => [a.getAttribute('href')?.split('/').pop(), a.textContent]),
    )

    expect(labelled['suppliers']).toBe('Suppliers 7')
    expect(labelled['proposals']).toBe('Bids 4')
    expect(labelled['my-evaluation']).toBe('My evaluation')
    expect(labelled['award']).toBe('Award')
    expect(labelled['settings']).toBe('Settings')
  })

  it('shows no count rather than a zero when the count cannot be read', async () => {
    restore?.()
    restore = mockFetch({})

    renderTabs()

    const nav = await screen.findByRole('navigation', { name: 'Tender sections' })
    const labelled = Object.fromEntries(
      within(nav).getAllByRole('link').map((a) => [a.getAttribute('href')?.split('/').pop(), a.textContent]),
    )

    expect(labelled['suppliers']).toBe('Suppliers')
    expect(labelled['proposals']).toBe('Bids')
    expect(within(nav).getAllByRole('link')).toHaveLength(7)
  })

  describe('offers each persona only the views it can open', () => {
    it('declares what every tab it can offer requires', () => {
      renderTabs()
      const views = offeredViews()

      expect(Object.keys(TAB_REQUIRES)).toHaveLength(7)
      expect(views).toHaveLength(Object.keys(TAB_REQUIRES).length)
      for (const view of views) {
        expect(Object.keys(TAB_REQUIRES), `the strip offers "${view}", which TAB_REQUIRES does not declare`).toContain(view)
      }
    })

    it.each(Object.keys(PERSONAS) as (keyof typeof PERSONAS)[])('offers %s exactly the views its permissions open', (persona) => {
      signInWith(PERSONAS[persona])
      renderTabs()

      expect([...offeredViews()].sort()).toEqual(viewsCoveredBy(PERSONAS[persona]).sort())
    })

    it('offers an evaluator their own scoring alone, marked as the page they are on', () => {
      pathname = `${TENDER}/my-evaluation`
      signInWith(PERSONAS.evaluator)
      renderTabs()

      expect(offeredViews()).toEqual(['my-evaluation'])
      const current = screen.getAllByRole('link').filter((a) => a.getAttribute('aria-current') === 'page')
      expect(current.map((a) => viewOf(a.getAttribute('href')))).toEqual(['my-evaluation'])
    })

    it('can hide every tab, and can hide none', () => {
      signInWith([])
      const empty = renderTabs()
      expect(screen.queryAllByRole('link')).toHaveLength(0)
      empty.unmount()

      signInWith(EVERY_PERMISSION)
      renderTabs()
      expect(offeredViews()).toHaveLength(7)
    })

    it('does not ask for the tender or its workspace on behalf of a reader who cannot read either', async () => {
      restore?.()
      const recorded: RecordedRequest[] = []
      restore = mockFetch(ROUTES, recorded)

      signInWith(PERSONAS.evaluator)
      const evaluator = renderTabs()
      await settle()
      expect(recorded.map((request) => request.url)).toEqual([])
      evaluator.unmount()

      signInWith(PERSONAS.procurement_officer)
      renderTabs()
      await settle()
      expect(recorded.map((request) => new URL(request.url, 'http://localhost').pathname).sort()).toEqual([
        '/api/v1/rfqs/RFQ-2026-000001',
        '/api/v1/rfqs/RFQ-2026-000001/workspace',
      ])
      await screen.findByText('7')
    })
  })
})
