import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage } from '../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, Link: 'a', useRouterState: () => '/onboarding' }
})

const { OnboardingPage } = await import('./OnboardingPage')

const supplier = {
  referenceCode: 'SUP-2026-000003',
  displayNameAr: 'شركة',
  displayNameEn: 'Resubmit Demo Co',
  description: 'seed', website: null, logoStorageKey: null, supplierGroup: null,
  onboardingState: 'InfoRequested',
  lifecycleState: 'Active',
  currencyCode: 'SYP', legalInfo: null, primaryContactPhone: '+963900000000',
  representatives: [], addresses: [], contacts: [], branches: [], bankAccounts: [], categoryCodes: [],
}

const annotation = {
  id: 'annotation-1',
  requestedAt: new Date().toISOString(),
  reason: 'Please fix the description.',
  flaggedProfileFields: ['description'],
  flaggedDocumentTypeCodes: [],
  resolvedAt: null,
}

/**
 * Task #19: resubmitMutation's onSuccess (queryClient.invalidateQueries, now invalidateQuietly)
 * was one of the eight no-floating-promises findings, and the last one not otherwise exercised by
 * a test in this PR. resubmitMutation shows no success toast (only onProfile + invalidate), so this
 * counts GET calls to the annotation endpoint - the same technique used for
 * ReviewApplicationPage.pickup.test.tsx - rather than reusing the shared mockFetch harness, which
 * always returns the same declared body and cannot distinguish "refetched" from "never fetched
 * again".
 */
describe('OnboardingPage resubmit flow', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('re-fetches the active annotation after a successful resubmit', async () => {
    const original = globalThis.fetch
    let annotationGetCount = 0

    globalThis.fetch = (async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
      if (url.includes('/api/v1/suppliers/me/resubmit-application')) {
        return new Response(JSON.stringify({ ...supplier, onboardingState: 'Resubmitted' }), { status: 200 })
      }
      if (url.includes('/api/v1/suppliers/me/active-annotation')) {
        annotationGetCount += 1
        return new Response(JSON.stringify(annotation), { status: 200 })
      }
      if (url.includes('/api/v1/suppliers/SUP-2026-000001/documents')) {
        return new Response(JSON.stringify([]), { status: 200 })
      }
      if (url.includes('/api/v1/currencies')) {
        return new Response(JSON.stringify([]), { status: 200 })
      }
      if (url.includes('/api/v1/suppliers/me')) {
        return new Response(JSON.stringify(supplier), { status: 200 })
      }
      throw new Error(`No mock declared for ${url}`)
    }) as typeof fetch
    restore = () => { globalThis.fetch = original }

    renderPage(<OnboardingPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Resubmit' }))

    await waitFor(() => expect(annotationGetCount).toBeGreaterThanOrEqual(2))
  })
})
