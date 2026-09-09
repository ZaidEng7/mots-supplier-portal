import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch, type RecordedRequest } from '../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, Link: 'a', useRouterState: () => '/onboarding' }
})

const { OnboardingPage } = await import('./OnboardingPage')

const supplier = {
  supplierCode: 'SUP-2026-000003',
  displayNameAr: 'شركة',
  displayNameEn: 'Save Demo Co',
  description: null, website: null, logoStorageKey: null, supplierGroup: null,
  onboardingState: 'ProfileInProgress',
  lifecycleState: 'Active',
  currencyCode: null, primaryContactPhone: null,
  legalInfo: {
    legalNameAr: 'الاسم', legalNameEn: 'Legal Name',
    registrationNumber: 'RC-1', taxId: 'TX-1',
    supplierType: 'Company', establishedOn: null,
  },
  missingProfileFields: [],
  representatives: [], addresses: [], contacts: [], branches: [], bankAccounts: [], categoryCodes: [],
}

const ROUTES = {
  '/api/v1/suppliers/me': supplier,
  '/api/v1/suppliers/SUP-2026-000003/documents': [],
  '/api/v1/suppliers/me/annotations/active': null,
  '/api/v1/currencies': [{ code: 'SYP', nameAr: 'ليرة', nameEn: 'Syrian Pound' }],
}

/**
 * The two saves on the supplier's own application, which nothing exercised.
 *
 * <p>Four test files already drive this screen — uploads, documents, resubmission, and what is still
 * missing — and between them they never submit either form. So the legal-information save and the
 * profile save, the two writes this screen exists for, were covered only by the fact that they compile.
 * A handler wired to the wrong mutation, or one that never fires because its form has no submit button
 * inside it, would have looked exactly like this.</p>
 *
 * <p>Found while reading a coverage report rather than by design: moving these forms inside `Card` in
 * the 6D pass made their lines "new", and the gate asked why nothing ran them.</p>
 */
describe('OnboardingPage saves', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('sends the legal information the supplier typed, to the legal-info endpoint', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch(ROUTES, recorded)

    renderPage(<OnboardingPage />)

    const nameEn = await screen.findByLabelText(/Legal name \(English\)/i)
    await userEvent.clear(nameEn)
    await userEvent.type(nameEn, 'Renamed Co')
    await userEvent.click(screen.getByRole('button', { name: 'Save legal information' }))

    await waitFor(() => {
      const write = recorded.find((r) => r.url.includes('/legal-info') && r.method !== 'GET')
      expect(write, 'the legal-info save never reached the wire').toBeDefined()
      expect(JSON.parse(String(write!.body)).legalNameEn).toBe('Renamed Co')
    })
  })

  it('sends the profile the supplier typed, to the supplier endpoint', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch(ROUTES, recorded)

    renderPage(<OnboardingPage />)

    const website = await screen.findByLabelText(/Website/i)
    await userEvent.clear(website)
    await userEvent.type(website, 'https://example.test')
    await userEvent.click(screen.getByRole('button', { name: 'Save profile' }))

    await waitFor(() => {
      const write = recorded.find((r) => r.url.includes('/suppliers/SUP-2026-000003') && r.method !== 'GET')
      expect(write, 'the profile save never reached the wire').toBeDefined()
      expect(JSON.parse(String(write!.body)).website).toBe('https://example.test')
    })
  })
})
