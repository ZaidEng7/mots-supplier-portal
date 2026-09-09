import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import { renderPage, mockFetch, expectRetryableFailure, type RecordedRequest } from '../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, Link: 'a' }
})

const { ProfilePage } = await import('./ProfilePage')

function profile(overrides: Record<string, unknown> = {}) {
  return {
    id: 's-1',
    supplierCode: 'SUP-000001',
    displayNameEn: 'Gulf Catering Co.',
    displayNameAr: 'شركة الخليج للتموين',
    onboardingState: 'Approved',
    lifecycleState: 'Active',
    missingProfileFields: [],
    legalInfo: null,
    representatives: [],
    addresses: [],
    bankAccounts: [],
    categories: [],
    ...overrides,
  }
}

/**
 * SCR-121. This page is the supplier's only READ of their own profile, and it renders eight sections
 * off one DTO - so the failures worth pinning are the ones that take the whole page down rather than
 * one card: an absent nested object, and an array the API did not send.
 *
 * That second case is not hypothetical. The e2e fixture omitted `categories` and this page threw on
 * `.length` during render, which surfaced as an a11y-suite failure on an unrelated assertion.
 */
describe('ProfilePage (SCR-121)', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('renders the profile with its code and lifecycle state', async () => {
    restore = mockFetch({ '/api/v1/suppliers/me': profile() })

    renderPage(<ProfilePage />)

    expect(await screen.findByText('Gulf Catering Co.')).toBeInTheDocument()
    expect(screen.getByText('SUP-000001')).toBeInTheDocument()
  })

  it('renders every section when the profile is fully populated', async () => {
    restore = mockFetch({
      '/api/v1/suppliers/me': profile({
        legalInfo: { legalName: 'Gulf Catering Company LLC', registrationNumber: 'CR-1234' },
        representatives: [{ id: 'r-1', fullName: 'Layla Hassan', email: 'layla@example.test', phone: null, isPrimary: true }],
        addresses: [{ id: 'a-1', kind: 'Head office', line1: '12 Corniche Rd', line2: null, city: 'Muscat', regionCode: 'MC', country: 'OM' }],
        bankAccounts: [{ id: 'b-1', bankName: 'Bank Muscat', maskedAccountNumber: '****4417', currencyCode: 'OMR', isDefault: true }],
        categories: ['catering', 'logistics'],
      }),
    })

    renderPage(<ProfilePage />)

    expect(await screen.findByText('Layla Hassan')).toBeInTheDocument()
    expect(screen.getByText('****4417')).toBeInTheDocument()
    expect(screen.getByText('catering')).toBeInTheDocument()
    // The address is joined from the parts that are present, so a null line2 must not leave ", ,".
    expect(screen.getByText(/12 Corniche Rd, Muscat, MC, OM/)).toBeInTheDocument()
  })

  it('renders a profile whose legalInfo is null rather than throwing', async () => {
    // legalInfo is absent for a supplier who has not reached that step, and this page reads fields off
    // it for every row in LEGAL_INFO_FIELDS. Guarding at the section, not per field, is what keeps that
    // from being eight separate crashes.
    restore = mockFetch({ '/api/v1/suppliers/me': profile({ legalInfo: null }) })

    renderPage(<ProfilePage />)

    expect(await screen.findByText('Gulf Catering Co.')).toBeInTheDocument()
    expect(screen.getAllByText('—').length).toBeGreaterThan(0)
  })

  it('lists the missing fields the server named, not a list of its own', async () => {
    // The completeness list comes from the server's missingProfileFields, so the screen cannot disagree
    // with the gate that will refuse the submission. A second client-side derivation would drift.
    restore = mockFetch({
      '/api/v1/suppliers/me': profile({ onboardingState: 'Draft', lifecycleState: 'None', missingProfileFields: ['legalInfo', 'categoryLink', 'termsAccepted'] }),
    })

    renderPage(<ProfilePage />)

    // Scoped to the badge list, because the same labels are also the <dt> text of the sections below:
    // asserting on the page as a whole would pass even if the completeness card rendered nothing.
    await screen.findByText('Gulf Catering Co.')
    const badges = screen.getAllByRole('listitem').map((li) => li.textContent)
    // The codes are the ones Supplier.GetMissingProfileFields actually emits, so this also pins that
    // every one of them has a translation: an untranslated code falls back to itself and the supplier
    // is told to go and fix "categoryLink".
    expect(badges).toContain('Legal information')
    expect(badges).toContain('Categories')
    expect(badges).toContain('Terms accepted')
  })

  it('offers a retry rather than a blank page when the profile cannot be read', async () => {
    restore = mockFetch({ '/api/v1/suppliers/me': { __status: 500 } })

    renderPage(<ProfilePage />)

    expect(await screen.findByRole('button', { name: /try again|إعادة المحاولة/i })).toBeInTheDocument()
  })

  it('shows a retryable failure rather than an empty screen', async () => {
    // Two halves that nothing asserted before: that the error branch RENDERS, and that the control
    // inside it does anything. `asyncStateCoverage` proves the branch exists in the source; a retry
    // button wired to nothing looks identical to one that works.
    const recorded: RecordedRequest[] = []
    restore = mockFetch({ '/api/v1/suppliers/me': { __status: 500 } }, recorded)

    renderPage(<ProfilePage />)

    await expectRetryableFailure('/api/v1/suppliers/me', recorded)
  })
})
