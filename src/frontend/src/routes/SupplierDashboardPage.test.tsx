// SCR-120, the supplier dashboard. The fixture has no decided bids unless a test says otherwise, which is the state a new
// supplier is in (T-039).
//
// §1's four KPI tiles, the invitation and the proposal all render. The invitation status renders as a LABEL rather than
// the raw enum: InvitationStatus had no §7 table, so this chip used to fall back to the wire value, and the whole point of
// Phase 0 is that "Responding" is now a label in both languages.
//
// NOT-YET-APPROVED replaces the dashboard rather than showing zeroes, per §1's "dashboard replaced by onboarding progress
// banner linking to SCR-100": a supplier who is not yet eligible for any invitation must not read "Open invitations: 0"
// as a verdict. An APPROVED supplier with no invitations gets the empty state rather than a blank list.
//
// THE ISOLATED FAILURE is §1's "per-widget ErrorPanel + retry (isolated failures don't blank the page)". It is the state
// most easily built as a page-level error, which would pass a naive "shows an error" test while breaking the requirement -
// so the assertion is that the OTHER widgets survived, and the three that must still be standing are named. The widget
// renders the shared QueryError now, so the wording is common.loadFailed rather than a per-widget copy of the same
// sentence; what the test is about is unchanged. The widget-level retry is asserted separately, because it is a different
// code path from the whole-page one.
//
// The ERP-degraded banner is subtle and leaves the rest of the page unaffected. An action chip can be dismissed and only
// that chip goes - the control being that dismissing one must not clear the strip.
//
// THE COMPLETENESS METER reports its numerator and denominator: §12.2 shows profileCompleteness and nothing produces it,
// so the ratio is computed - and the screen shows what it counted rather than a bare percentage nobody can check. Counts
// and the meter render Eastern Arabic numerals under Arabic.
//
// THE NEXT REQUIRED DOCUMENT is NAMED instead of shown as its code. Found by reading this dashboard as the supplier: the
// caption said "Next required document: commercial_registration" - a database value, on the one line that tells them what
// to do next. The control is a real case: a document type could be renamed or removed while a supplier's requirement still
// points at it, and a blank caption would be worse than the code.
//
// A whole-page failure offers a retry.
//
// THE AWARD RESULTS include the ones the supplier lost. FEAT-16.3's acceptance is "award outcomes shown", and the
// proposals panel deliberately excludes NotSelected - so before this widget a supplier who lost watched their bid vanish
// from the screen they open first, with no outcome anywhere on it. With nothing decided the widget SAYS so rather than
// hiding: an absent panel and an empty one say different things to a supplier waiting on a result, the first reading as
// "this product does not tell you" and the second as "not yet".
//
// The last test is T-039's other branch: a bid that was never priced, with value and currency both absent. A bid can be
// declined or rejected before anyone priced it, and "-" is the truth there while "SYP 0" is a number somebody would have
// had to quote - so there is no currency string anywhere in the row, the amount being absent rather than zero.

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import i18n from '../i18n/config'
import { renderPage, mockFetch, expectRetryableFailure, type RecordedRequest } from '../test/renderPage'
import { clearDismissed } from '../lib/dismissedChips'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, Link: 'a' }
})

const { SupplierDashboardPage } = await import('./SupplierDashboardPage')

function dashboard(overrides: Record<string, unknown> = {}) {
  return {
    supplierReferenceCode: 'SUP-2026-000001',
    displayNameAr: 'شركة الاختبار', displayNameEn: 'Test Co',
    onboardingState: 'Approved', lifecycleState: 'Active',
    isApproved: true,
    kpis: { openInvitations: 3, draftProposals: 1, submittedProposals: 2, documentsNeedingAttention: 4 },
    actionRequired: {
      expiringDocuments: 2, rejectedDocuments: 0, invitationsClosingSoon: 1,
      clarificationsAnswered: 0, awardOffers: 0,
    },
    invitations: [
      {
        rfqReferenceCode: 'RFQ-2026-000001', titleAr: 'طلب تموين', titleEn: 'Catering RFQ',
        invitationStatus: 'Responding', submissionClosesAt: '2026-09-30T12:00:00Z',
      },
    ],
    proposals: [
      {
        proposalReferenceCode: 'PRP-2026-000001', rfqReferenceCode: 'RFQ-2026-000001',
        titleAr: 'طلب تموين', titleEn: 'Catering RFQ', state: 'Draft', validityEnd: '2026-10-30',
      },
    ],
    profileHealth: {
      completeness: 0.5, requiredDocumentsTotal: 4, requiredDocumentsSupplied: 2,
      nextRequiredDocumentTypeCode: 'commercial_registration',
      nextRequiredDocumentNameAr: 'السجل التجاري', nextRequiredDocumentNameEn: 'Commercial Registration',
    },
    erpDegraded: false,
    awards: [],
    ...overrides,
  }
}

describe('SupplierDashboardPage (SCR-120)', () => {
  let restore: () => void
  beforeEach(() => clearDismissed())
  afterEach(() => restore?.())

  it('ok: renders §1\'s four KPI tiles, the invitation and the proposal', async () => {
    restore = mockFetch({
      '/api/v1/suppliers/me/dashboard': dashboard(),
      '/api/v1/notifications/unread-count': { count: 2 },
    })

    renderPage(<SupplierDashboardPage />)

    expect(await screen.findByText('Open invitations')).toBeInTheDocument()
    for (const tile of ['Draft proposals', 'Submitted proposals', 'Documents needing attention']) {
      expect(screen.getByText(tile)).toBeInTheDocument()
    }
    expect(screen.getAllByText('Catering RFQ')).not.toHaveLength(0)
  })

  it('renders the invitation status as a label, not the raw enum', async () => {
    restore = mockFetch({
      '/api/v1/suppliers/me/dashboard': dashboard(),
      '/api/v1/notifications/unread-count': { count: 0 },
    })

    renderPage(<SupplierDashboardPage />)

    expect(await screen.findByText('Responding')).toBeInTheDocument()
  })

  it('not-yet-approved: replaces the dashboard rather than showing zeroes', async () => {
    restore = mockFetch({
      '/api/v1/suppliers/me/dashboard': dashboard({ isApproved: false, onboardingState: 'UnderReview' }),
      '/api/v1/notifications/unread-count': { count: 0 },
    })

    renderPage(<SupplierDashboardPage />)

    expect(await screen.findByText('Your application is under review')).toBeInTheDocument()
    expect(screen.queryByText('Open invitations')).not.toBeInTheDocument()
  })

  it('empty: an approved supplier with no invitations gets the empty state, not a blank list', async () => {
    restore = mockFetch({
      '/api/v1/suppliers/me/dashboard': dashboard({
        invitations: [], proposals: [],
        kpis: { openInvitations: 0, draftProposals: 0, submittedProposals: 0, documentsNeedingAttention: 0 },
        actionRequired: { expiringDocuments: 0, rejectedDocuments: 0, invitationsClosingSoon: 0, clarificationsAnswered: 0, awardOffers: 0 },
      }),
      '/api/v1/notifications/unread-count': { count: 0 },
    })

    renderPage(<SupplierDashboardPage />)

    expect(await screen.findByText('No invitations yet')).toBeInTheDocument()
  })

  it('error is isolated per widget: the notification panel fails and everything else stands', async () => {
    const original = globalThis.fetch
    globalThis.fetch = (async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
      if (url.includes('/notifications/unread-count')) return new Response('{}', { status: 500 })
      if (url.includes('/suppliers/me/dashboard')) return new Response(JSON.stringify(dashboard()), { status: 200 })
      throw new Error(`No mock declared for ${url}`)
    }) as typeof fetch
    restore = () => { globalThis.fetch = original }

    renderPage(<SupplierDashboardPage />)

    expect(await screen.findByText('We could not load this. Try again.')).toBeInTheDocument()

    expect(screen.getByText('Open invitations')).toBeInTheDocument()
    expect(screen.getAllByText('Catering RFQ')).not.toHaveLength(0)
    expect(screen.getByText(// The NAME now, not the code: that caption is the one line telling a supplier what to do next, and it
      'Next required document: Commercial Registration')).toBeInTheDocument()
  })

  it('erp-degraded: a subtle banner, and the rest of the page unaffected', async () => {
    restore = mockFetch({
      '/api/v1/suppliers/me/dashboard': dashboard({ erpDegraded: true }),
      '/api/v1/notifications/unread-count': { count: 0 },
    })

    renderPage(<SupplierDashboardPage />)

    expect(await screen.findByText('Purchase-order sync is paused. This does not affect your proposal.')).toBeInTheDocument()
    expect(screen.getByText('Open invitations')).toBeInTheDocument()
  })

  it('an action chip can be dismissed, and only that chip goes', async () => {
    restore = mockFetch({
      '/api/v1/suppliers/me/dashboard': dashboard(),
      '/api/v1/notifications/unread-count': { count: 0 },
    })

    renderPage(<SupplierDashboardPage />)

    expect(await screen.findByText('Documents expiring (2)')).toBeInTheDocument()
    expect(screen.getByText('Invitations closing soon (1)')).toBeInTheDocument()

    await userEvent.click(screen.getAllByRole('button', { name: 'Dismiss' })[0])

    expect(screen.queryByText('Documents expiring (2)')).not.toBeInTheDocument()
    expect(screen.getByText('Invitations closing soon (1)')).toBeInTheDocument()
  })

  it('the completeness meter reports its numerator and denominator', async () => {
    restore = mockFetch({
      '/api/v1/suppliers/me/dashboard': dashboard(),
      '/api/v1/notifications/unread-count': { count: 0 },
    })

    renderPage(<SupplierDashboardPage />)

    expect(await screen.findByText('Required documents: 2 of 4')).toBeInTheDocument()
    expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '50')
  })

  it('counts and the meter render Eastern Arabic numerals under Arabic', async () => {
    const restoreFetch = mockFetch({
      '/api/v1/suppliers/me/dashboard': dashboard(),
      '/api/v1/notifications/unread-count': { count: 0 },
    })
    await i18n.changeLanguage('ar')
    restore = () => { restoreFetch(); void i18n.changeLanguage('en') }

    renderPage(<SupplierDashboardPage />)

    expect(await screen.findByText('٣')).toBeInTheDocument()
    expect(screen.getByText('اكتمال الوثائق المطلوبة: ٢ من ٤')).toBeInTheDocument()
  })

  it('names the next required document instead of showing its code', async () => {
    restore = mockFetch({
      '/api/v1/suppliers/me/dashboard': dashboard({
        profileHealth: {
          completeness: 0.5,
          requiredDocumentsTotal: 2,
          requiredDocumentsSupplied: 0,
          nextRequiredDocumentTypeCode: 'commercial_registration',
          nextRequiredDocumentNameAr: 'السجل التجاري',
          nextRequiredDocumentNameEn: 'Commercial Registration',
        },
      }),
      '/api/v1/notifications/unread-count': { count: 0 },
    })

    renderPage(<SupplierDashboardPage />)

    expect(await screen.findByText(/Commercial Registration/)).toBeInTheDocument()
    expect(screen.queryByText(/commercial_registration/)).not.toBeInTheDocument()
  })

  it('falls back to the code when the reference row is gone', async () => {
    restore = mockFetch({
      '/api/v1/suppliers/me/dashboard': dashboard({
        profileHealth: {
          completeness: 0.5,
          requiredDocumentsTotal: 2,
          requiredDocumentsSupplied: 0,
          nextRequiredDocumentTypeCode: 'orphan_type',
          nextRequiredDocumentNameAr: null,
          nextRequiredDocumentNameEn: null,
        },
      }),
      '/api/v1/notifications/unread-count': { count: 0 },
    })

    renderPage(<SupplierDashboardPage />)

    expect(await screen.findByText(/orphan_type/)).toBeInTheDocument()
  })

  it('shows a retryable failure when the dashboard itself cannot load', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch({
      '/api/v1/suppliers/me/dashboard': { __status: 500 },
      '/api/v1/notifications/unread-count': { count: 0 },
    }, recorded)

    renderPage(<SupplierDashboardPage />)

    await expectRetryableFailure('/suppliers/me/dashboard', recorded)
  })

  it('the isolated notifications failure offers its own retry', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch({
      '/api/v1/suppliers/me/dashboard': dashboard(),
      '/api/v1/notifications/unread-count': { __status: 500 },
    }, recorded)

    renderPage(<SupplierDashboardPage />)

    await expectRetryableFailure('/notifications/unread-count', recorded)
    expect(screen.getByText('Open invitations')).toBeInTheDocument()
  })

  it('shows award results, including the ones the supplier lost', async () => {
    restore = mockFetch({
      '/api/v1/suppliers/me/dashboard': dashboard({
        awards: [
          {
            rfqReferenceCode: 'RFQ-2026-000009', rfqTitleAr: 'توريد', rfqTitleEn: 'Catering for the summit',
            proposalCode: 'PRO-2026-000009', outcome: 'Awarded',
            decidedAt: '2026-09-01T10:00:00Z', value: 125000, currencyCode: 'SYP',
          },
          {
            rfqReferenceCode: 'RFQ-2026-000010', rfqTitleAr: 'نقل', rfqTitleEn: 'Transport for the delegation',
            proposalCode: 'PRO-2026-000010', outcome: 'NotSelected',
            decidedAt: '2026-08-20T10:00:00Z', value: 88000, currencyCode: 'SYP',
          },
        ],
      }),
      '/api/v1/notifications/unread-count': { count: 0 },
    })

    renderPage(<SupplierDashboardPage />)

    expect(await screen.findByText('Award results')).toBeInTheDocument()
    expect(screen.getByText('Catering for the summit')).toBeInTheDocument()
    expect(screen.getByText('Transport for the delegation')).toBeInTheDocument()
  })

  it('says so when nothing has been decided, rather than hiding the widget', async () => {
    restore = mockFetch({
      '/api/v1/suppliers/me/dashboard': dashboard(),
      '/api/v1/notifications/unread-count': { count: 0 },
    })

    renderPage(<SupplierDashboardPage />)

    expect(await screen.findByText('None of your bids has been decided yet.')).toBeInTheDocument()
  })

  it('shows an outcome for a bid that was never priced, without inventing a zero', async () => {
    restore = mockFetch({
      '/api/v1/suppliers/me/dashboard': dashboard({
        awards: [{
          rfqReferenceCode: 'RFQ-2026-000011', rfqTitleAr: 'خدمة', rfqTitleEn: 'Unpriced bid',
          proposalCode: 'PRO-2026-000011', outcome: 'NotSelected',
          decidedAt: null, value: null, currencyCode: null,
        }],
      }),
      '/api/v1/notifications/unread-count': { count: 0 },
    })

    renderPage(<SupplierDashboardPage />)

    expect(await screen.findByText('Unpriced bid')).toBeInTheDocument()
    expect(screen.queryByText(/SYP/)).not.toBeInTheDocument()
  })
})
