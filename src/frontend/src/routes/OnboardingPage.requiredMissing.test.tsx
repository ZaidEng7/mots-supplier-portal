import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage } from '../test/renderPage'
import i18n from '../i18n/config'
import type { DocumentTypeStatus } from '../api/documents'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, Link: 'a', useRouterState: () => '/onboarding' }
})

const { OnboardingPage } = await import('./OnboardingPage')

function documentType(code: string, isRequired: boolean): DocumentTypeStatus {
  return {
    documentTypeId: `id-${code}`,
    code,
    nameAr: `وثيقة ${code}`,
    nameEn: code,
    isRequired,
    expiryTracked: false,
    latestDocument: null,
  }
}

const supplier = {
  referenceCode: 'SUP-2026-000001',
  displayNameAr: 'شركة', displayNameEn: 'Required Missing Co',
  description: null, website: null, logoStorageKey: null, supplierGroup: null,
  onboardingState: 'ProfileInProgress',
  lifecycleState: 'Active',
  currencyCode: null, legalInfo: null, primaryContactPhone: null,
  missingProfileFields: [],
  representatives: [], addresses: [], contacts: [], branches: [], bankAccounts: [], categoryCodes: [],
}

const DOCUMENTS = [documentType('commercial_registration', true), documentType('chamber_membership', false)]

/**
 * `mockFetch` answers by URL alone, and this flow needs the SAME url to answer differently
 * depending on method and call order: the submit POST must fail with the server's 422, while the
 * GETs keep succeeding. Hand-rolled for that reason.
 *
 * @param missingFields what the server names in its 422 - the real contract, API-ARCHITECTURE §12.2:
 * *"Incomplete required docs/fields → `422` listing exactly what is missing"*.
 */
function mockApi(missingFields: string[]) {
  const original = globalThis.fetch
  globalThis.fetch = ((input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
    const method = (init?.method ?? 'GET').toUpperCase()

    const json = (body: unknown, status = 200) =>
      Promise.resolve(new Response(JSON.stringify(body), {
        status, headers: { 'Content-Type': 'application/json' },
      }))

    if (url.includes('/onboarding/submit') && method === 'POST') {
      return json({ error: 'incomplete_profile', missingFields }, 422)
    }
    // §12-A/C3: supplier routes are code-addressed; ordered most-specific first because
    // `/suppliers/me` is still a real route this page reads the code from.
    if (url.includes('/documents')) return json(DOCUMENTS)
    if (url.includes('/annotations/active')) return json(null)
    if (url.includes('/suppliers/me')) return json(supplier)
    if (url.includes('/currencies')) return json([])
    return json({})
  }) as typeof fetch

  return () => { globalThis.fetch = original }
}

/**
 * The product owner's ruling, in two states: a required document nobody has uploaded rests as
 * `Required`/مطلوب (UX-WRITING §7.2's first row, and the first entry in SCR-106's StatusBadge set),
 * and becomes `Missing` only once the supplier has tried to advance and it is still absent.
 *
 * <p><b>Not a `DocumentState`.</b> Neither label is a member of the document state machine - this is
 * a validation display state, driven by the server's own 422 list rather than by a client-side
 * completeness rule that could disagree with the server about what actually blocks submission.</p>
 */
describe('required-vs-missing document chips', () => {
  let restore: () => void
  afterEach(async () => {
    restore?.()
    await i18n.changeLanguage('en')
  })

  it('rests as Required, not Missing, before any submit attempt', async () => {
    restore = mockApi(['commercial_registration'])

    renderPage(<OnboardingPage />)

    const heading = await screen.findByRole('heading', { name: 'Required documents' })
    const section = heading.closest('section')!

    expect(within(section).getByText('Required')).toBeInTheDocument()
    expect(within(section).queryByText('Missing')).toBeNull()
  })

  it('renders the Arabic label مطلوب under the ar locale', async () => {
    await i18n.changeLanguage('ar')
    restore = mockApi(['commercial_registration'])

    renderPage(<OnboardingPage />)

    expect(await screen.findByText('مطلوب')).toBeInTheDocument()
    expect(screen.queryByText('ناقص')).toBeNull()
  })

  /**
   * The escalation, and its limit: only the documents the SERVER named change. An optional document
   * is untouched (SCR-106: "optional docs never block"), and so would a required one the server did
   * not list - which is what stops this from being a client-side "everything empty is missing" rule.
   */
  it('escalates only the documents the server named, after a failed submit', async () => {
    restore = mockApi(['commercial_registration'])

    renderPage(<OnboardingPage />)
    await userEvent.click(await screen.findByRole('button', { name: 'Submit application' }))

    const requiredSection = (await screen.findByRole('heading', { name: 'Required documents' })).closest('section')!
    await waitFor(() => expect(within(requiredSection).getByText('Missing')).toBeInTheDocument())
    expect(within(requiredSection).queryByText('Required')).toBeNull()

    const optionalSection = screen.getByRole('heading', { name: 'Optional documents' }).closest('section')!
    expect(within(optionalSection).queryByText('Missing')).toBeNull()
    expect(within(optionalSection).queryByText('Required')).toBeNull()
  })

  /**
   * ACCESSIBILITY.md §7: *"Error summary at submit: a focusable summary region (`role="alert"` or
   * moved focus) listing each error as a link jumping to its field - essential for long onboarding
   * forms and SR users."*
   *
   * <p>A colour change on a chip inside a long list announces nothing. This asserts the region
   * exists with the right role, names the blocking document, and links to that document's row -
   * all three, because a `role="alert"` that says "submission failed" and nothing else satisfies
   * the role and not the requirement.</p>
   */
  it('announces the blocking documents in an alert region that links to each row', async () => {
    restore = mockApi(['commercial_registration'])

    renderPage(<OnboardingPage />)
    await userEvent.click(await screen.findByRole('button', { name: 'Submit application' }))

    const alert = await screen.findByRole('alert')
    expect(within(alert).getByText('Your application cannot be submitted yet')).toBeInTheDocument()

    const link = within(alert).getByRole('link', { name: 'commercial_registration' })
    expect(link).toHaveAttribute('href', '#document-row-commercial_registration')
    expect(document.getElementById('document-row-commercial_registration')).not.toBeNull()
  })

  /**
   * The server's 422 mixes missing PROFILE fields into the same array as document type codes. A
   * profile field must not surface as a missing document, and must not escalate a chip.
   */
  it('ignores profile-field names in the 422 list when escalating documents', async () => {
    restore = mockApi(['legalNameEn'])

    renderPage(<OnboardingPage />)
    await userEvent.click(await screen.findByRole('button', { name: 'Submit application' }))

    // The toast still reports the failure; the document chips do not move.
    await screen.findByText('Profile incomplete')
    expect(screen.queryByRole('alert')).toBeNull()
    expect(screen.getByText('Required')).toBeInTheDocument()
    expect(screen.queryByText('Missing')).toBeNull()
  })
})
