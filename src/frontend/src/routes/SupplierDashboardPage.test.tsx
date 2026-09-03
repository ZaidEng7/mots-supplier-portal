import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import i18n from '../i18n/config'
import { renderPage, mockFetch } from '../test/renderPage'
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
    },
    erpDegraded: false,
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
    // InvitationStatus had no §7 table, so this chip used to fall back to the wire value. The whole
    // point of Phase 0 is that "Responding" is now a label in both languages.
    restore = mockFetch({
      '/api/v1/suppliers/me/dashboard': dashboard(),
      '/api/v1/notifications/unread-count': { count: 0 },
    })

    renderPage(<SupplierDashboardPage />)

    expect(await screen.findByText('Responding')).toBeInTheDocument()
  })

  it('not-yet-approved: replaces the dashboard rather than showing zeroes', async () => {
    // §1: "dashboard replaced by onboarding progress banner linking to SCR-100". A supplier who is
    // not yet eligible for any invitation must not read "Open invitations: 0" as a verdict.
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
    // §1: "per-widget ErrorPanel + retry (isolated failures don't blank the page)". This is the
    // state most easily built as a page-level error, which would pass a naive "shows an error" test
    // while breaking the requirement - so the assertion is that the OTHER widgets survived.
    const original = globalThis.fetch
    globalThis.fetch = (async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
      if (url.includes('/notifications/unread-count')) return new Response('{}', { status: 500 })
      if (url.includes('/suppliers/me/dashboard')) return new Response(JSON.stringify(dashboard()), { status: 200 })
      throw new Error(`No mock declared for ${url}`)
    }) as typeof fetch
    restore = () => { globalThis.fetch = original }

    renderPage(<SupplierDashboardPage />)

    expect(await screen.findByText("Couldn't load this section")).toBeInTheDocument()

    // The three that must still be standing.
    expect(screen.getByText('Open invitations')).toBeInTheDocument()
    expect(screen.getAllByText('Catering RFQ')).not.toHaveLength(0)
    expect(screen.getByText('Next required document: commercial_registration')).toBeInTheDocument()
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
    // The control: dismissing one chip must not clear the strip.
    expect(screen.getByText('Invitations closing soon (1)')).toBeInTheDocument()
  })

  it('the completeness meter reports its numerator and denominator', async () => {
    // §12.2 shows profileCompleteness and nothing produces it, so the ratio is computed - and the
    // screen shows what it counted rather than a bare percentage nobody can check.
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
})
