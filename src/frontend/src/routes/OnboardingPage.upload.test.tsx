import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch } from '../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, Link: 'a', useRouterState: () => '/onboarding' }
})

const { OnboardingPage } = await import('./OnboardingPage')

const supplier = {
  referenceCode: 'SUP-2026-000002',
  displayNameAr: 'شركة',
  displayNameEn: 'Upload Demo Co',
  description: null, website: null, logoStorageKey: null, supplierGroup: null,
  onboardingState: 'ProfileInProgress',
  lifecycleState: 'Active',
  currencyCode: null, legalInfo: null, primaryContactPhone: null,
  representatives: [], addresses: [], contacts: [], branches: [], bankAccounts: [], categoryCodes: [],
}

/**
 * Task #19: DocumentRow's uploadMutation.onSuccess (queryClient.invalidateQueries, now
 * invalidateQuietly) was one of the eight no-floating-promises findings. Drives a real upload
 * through the page so the callback actually runs.
 */
describe('OnboardingPage document upload flow', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('shows a success toast once a document is uploaded', async () => {
    restore = mockFetch({
      '/api/v1/suppliers/SUP-2026-000002/documents': [{
        documentTypeId: 'id-commercial_registration',
        code: 'commercial_registration',
        nameAr: 'commercial_registration',
        nameEn: 'commercial_registration',
        isRequired: true,
        expiryTracked: false,
        latestDocument: null,
      }],
      '/api/v1/suppliers/me/annotations/active': null,
      '/api/v1/suppliers/me': supplier,
      '/api/v1/currencies': [],
    })

    const { container } = renderPage(<OnboardingPage />)

    await screen.findByText('commercial_registration')
    // Scoped by accept attribute, not a bare input[type=file] query - LogoUploader renders its own
    // hidden file input earlier in the DOM, and a query that grabbed that one instead would upload
    // to the wrong (unmocked) endpoint and fail silently into uploadMutation's onError.
    const fileInput = container.querySelector('input[type="file"][accept*=".pdf"]') as HTMLInputElement
    const file = new File(['content'], 'registration.pdf', { type: 'application/pdf' })

    await userEvent.upload(fileInput, file)

    expect(await screen.findByText('Document uploaded')).toBeInTheDocument()
  })
})
