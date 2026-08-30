import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage } from '../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, useParams: () => ({ referenceCode: 'SUP-2026-000039' }), Link: 'a' }
})

const { ReviewApplicationPage } = await import('./ReviewApplicationPage')

const view = {
  supplier: {
    referenceCode: 'SUP-2026-000039',
    displayNameAr: 'شركة',
    displayNameEn: 'Pickup Demo Co',
    description: null, website: null, logoStorageKey: null, supplierGroup: null,
    onboardingState: 'Submitted',
    lifecycleState: 'None',
    currencyCode: null, legalInfo: null, primaryContactPhone: null,
    representatives: [], addresses: [], contacts: [], branches: [], bankAccounts: [], categoryCodes: [],
  },
  erpSync: { status: 'NotSynced', lastSyncedAt: null, externalId: null },
  documents: [],
  annotationHistory: [],
}

/**
 * Task #19: pickUpMutation's onSuccess calls the shared `invalidate` helper, which contains two of
 * the eight no-floating-promises findings (now routed through invalidateQuietly). Nothing in this
 * file's sibling lifecycle test exercises a mutation success path.
 *
 * Counts GET calls rather than reusing the shared mockFetch harness, which always returns the same
 * declared body and so cannot distinguish "refetched" from "never fetched again" - a stateless mock
 * would make an assertion here pass whether or not invalidate() actually ran. The GET count rising
 * from 1 (initial load) to 2 (post-pickup refetch) is the one signal that actually proves it did.
 */
describe('ReviewApplicationPage pick-up flow', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('re-fetches the review view after a successful pick-up', async () => {
    const original = globalThis.fetch
    let getCount = 0

    globalThis.fetch = (async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
      if (url.includes('/pickup')) {
        return new Response(JSON.stringify({ ...view.supplier, onboardingState: 'UnderReview' }), { status: 200 })
      }
      if (url.includes('/api/v1/review/SUP-2026-000039')) {
        getCount += 1
        return new Response(JSON.stringify(view), { status: 200 })
      }
      throw new Error(`No mock declared for ${url}`)
    }) as typeof fetch
    restore = () => { globalThis.fetch = original }

    renderPage(<ReviewApplicationPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Start review' }))

    await waitFor(() => expect(getCount).toBeGreaterThanOrEqual(2))
  })
})
