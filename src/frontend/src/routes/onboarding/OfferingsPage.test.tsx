// The onboarding step where a supplier declares what they supply. The rule worth pinning is that the checkboxes go
// READ-ONLY outside the editable onboarding states: a supplier under review who can still change their categories is
// changing the basis on which they are being reviewed, and Approved is the same argument after the fact.
//
// Every category shows with the linked ones checked. Linking a category that was not linked and unlinking one that was
// both work - a link is a POST carrying the code in the body, an unlink a DELETE carrying it in the path.
//
// The page warns when the profile is incomplete for want of a category. With no categories configured it SAYS so rather
// than rendering an empty list: a reference table nobody has populated is an administrator's problem, and a supplier
// staring at blank space cannot tell it from a broken page.
//
// D-66's two halves close the file. A refused toggle says so instead of failing silently - the checkbox wrote, did not
// re-tick, and said nothing, on the one screen whose completion gates the whole application, reported twice from the
// walkthrough and still open until now. And a successful toggle RE-READS the profile rather than trusting the response
// alone, so the tick cannot depend on this particular response having carried the categories collection.
//
// The last test is the retryable failure rather than an empty category list.

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch, type RecordedRequest, expectRetryableFailure } from '../../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, Link: 'a', useRouterState: () => ({ location: { pathname: '/onboarding/offerings' } }) }
})

const { OfferingsPage } = await import('./OfferingsPage')

function profile(overrides: Record<string, unknown> = {}) {
  return {
    id: 's-1', supplierCode: 'SUP-000001',
    displayNameEn: 'Gulf Catering Co.', displayNameAr: 'الخليج',
    onboardingState: 'ProfileInProgress',
    categories: ['catering'],
    missingProfileFields: [],
    ...overrides,
  }
}

const CATEGORIES = [
  { code: 'catering', nameEn: 'Catering', nameAr: 'تموين' },
  { code: 'logistics', nameEn: 'Logistics', nameAr: 'خدمات لوجستية' },
]

describe('OfferingsPage', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('shows every category with the linked ones checked', async () => {
    restore = mockFetch({ '/api/v1/suppliers/me': profile(), '/api/v1/reference/categories': CATEGORIES })

    renderPage(<OfferingsPage />)

    const catering = await screen.findByRole('checkbox', { name: /catering|تموين/i })
    expect(catering).toBeChecked()
    expect(screen.getByRole('checkbox', { name: /logistics|لوجستية/i })).not.toBeChecked()
  })

  it.each(['EmailVerified', 'ProfileInProgress', 'InfoRequested'])(
    'allows editing in %s', async (onboardingState) => {
      restore = mockFetch({ '/api/v1/suppliers/me': profile({ onboardingState }), '/api/v1/reference/categories': CATEGORIES })

      renderPage(<OfferingsPage />)

      expect(await screen.findByRole('checkbox', { name: /logistics|لوجستية/i })).toBeEnabled()
    },
  )

  it.each(['UnderReview', 'Approved', 'Rejected'])(
    'refuses editing in %s', async (onboardingState) => {
      restore = mockFetch({ '/api/v1/suppliers/me': profile({ onboardingState }), '/api/v1/reference/categories': CATEGORIES })

      renderPage(<OfferingsPage />)

      expect(await screen.findByRole('checkbox', { name: /logistics|لوجستية/i })).toBeDisabled()
    },
  )

  it('links a category that was not linked, and unlinks one that was', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch({
      '/api/v1/suppliers/me': profile(),
      '/api/v1/reference/categories': CATEGORIES,
      '/api/v1/suppliers/me/category-links': profile({ categories: ['catering', 'logistics'] }),
      '/api/v1/suppliers/me/category-links/catering': profile({ categories: [] }),
    }, recorded)

    renderPage(<OfferingsPage />)

    await userEvent.click(await screen.findByRole('checkbox', { name: /logistics|لوجستية/i }))
    await userEvent.click(screen.getByRole('checkbox', { name: /catering|تموين/i }))

    const writes = recorded.filter((r) => r.method !== 'GET')
    const post = writes.find((r) => r.method === 'POST')
    expect(JSON.parse(post!.body)).toEqual({ categoryCode: 'logistics' })
    expect(writes.some((r) => r.method === 'DELETE' && r.url.endsWith('/category-links/catering'))).toBe(true)
  })

  it('warns when the profile is incomplete for want of a category', async () => {
    restore = mockFetch({
      '/api/v1/suppliers/me': profile({ categories: [], missingProfileFields: ['categoryLink'] }),
      '/api/v1/reference/categories': CATEGORIES,
    })

    renderPage(<OfferingsPage />)

    expect(await screen.findByRole('alert')).toBeInTheDocument()
  })

  it('says so when no categories are configured, rather than rendering an empty list', async () => {
    restore = mockFetch({ '/api/v1/suppliers/me': profile({ categories: [] }), '/api/v1/reference/categories': [] })

    renderPage(<OfferingsPage />)

    expect(await screen.findByText(/no categories|لا توجد فئات/i)).toBeInTheDocument()
  })

  it('says so when a toggle is refused, instead of failing silently', async () => {
    restore = mockFetch({
      '/api/v1/suppliers/me': profile({ categories: [] }),
      '/api/v1/reference/categories': CATEGORIES,
      '/api/v1/suppliers/me/category-links': { __status: 428 },
    })

    renderPage(<OfferingsPage />)

    await userEvent.click(await screen.findByRole('checkbox', { name: /catering/i }))

    expect(await screen.findByText('Could not update the category')).toBeInTheDocument()
  })

  it('re-reads the profile after a successful toggle rather than trusting the response alone', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch({
      '/api/v1/suppliers/me': profile({ categories: [] }),
      '/api/v1/reference/categories': CATEGORIES,
      '/api/v1/suppliers/me/category-links': profile({ categories: ['catering'] }),
    }, recorded)

    renderPage(<OfferingsPage />)
    await userEvent.click(await screen.findByRole('checkbox', { name: /catering/i }))

    await vi.waitFor(() => {
      const reads = recorded.filter((r) => r.method === 'GET' && r.url.endsWith('/api/v1/suppliers/me'))
      expect(reads.length).toBeGreaterThan(1)
    })
  })

  it('shows a retryable failure rather than an empty category list', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch({
      '/api/v1/suppliers/me': { __status: 500 },
      '/api/v1/reference/categories': CATEGORIES,
    }, recorded)

    renderPage(<OfferingsPage />)

    await expectRetryableFailure('/suppliers/me', recorded)
  })
})
