import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch, type RecordedRequest } from '../test/renderPage'

/*
  The query is in the URL now, so the tests supply one the way the router would.

  It was component state, which is why the top bar could not submit into this screen, why a search
  could not be linked to or reloaded, and why the back button left the screen rather than returning to
  the previous search. `q` here stands in for the address bar; `navigated` records what the form asked
  the router to do, which is the observable half of a submit.
*/
const routeQuery = { current: undefined as string | undefined }
const navigated: Array<Record<string, unknown>> = []

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return {
    ...actual,
    Link: 'a',
    useSearch: () => ({ q: routeQuery.current }),
    useNavigate: () => (options: Record<string, unknown>) => {
      navigated.push(options)
      const next = options.search as { q?: string } | undefined
      routeQuery.current = next?.q
    },
  }
})

const { SearchPage } = await import('./SearchPage')


function hit(overrides: Record<string, unknown> = {}) {
  return {
    kind: 'rfq',
    referenceCode: 'RFQ-2026-000006',
    titleAr: 'طلب تموين',
    titleEn: 'Catering RFQ',
    context: null,
    rank: 0.9,
    ...overrides,
  }
}

/**
 * SCR-906. The behaviour worth pinning here is the parts a browser check cannot hold still: that the
 * search is SUBMITTED rather than live, that a truncated result set says so, and that a kind with no
 * page of its own renders as plain text instead of a dead link.
 */
describe('SearchPage (SCR-906)', () => {
  let restore: () => void
  beforeEach(() => {
    routeQuery.current = undefined
    navigated.length = 0
  })
  afterEach(() => restore?.())

  /**
   * The naming defect, in the place a reader met it most.
   *
   * <p>The server puts three unrelated values in one `context` field - an RFQ's state, a supplier's
   * lifecycle state, and for an offering the supplier's code - and the screen printed it raw. So a
   * search answered with `InternalReview` and `SubmissionOpen`, enum members in English, on an Arabic
   * page as readily as an English one. Every one of those states already had an authored label.</p>
   */
  it('names a tender state in words rather than printing the enum member', async () => {
    restore = mockFetch({
      '/api/v1/search': {
        query: 'catering',
        hits: [hit({ context: 'InternalReview' })],
        truncated: false,
      },
    })
    routeQuery.current = 'catering'

    renderPage(<SearchPage />)

    expect(await screen.findByText('Internal review')).toBeInTheDocument()
    expect(screen.queryByText('InternalReview')).toBeNull()
  })

  it('names a supplier lifecycle state the same way', async () => {
    restore = mockFetch({
      '/api/v1/search': {
        query: 'barada',
        hits: [hit({ kind: 'supplier', titleEn: 'Barada Supplies', context: 'UnderReview' })],
        truncated: false,
      },
    })
    routeQuery.current = 'barada'

    renderPage(<SearchPage />)

    expect(await screen.findByText('Under review')).toBeInTheDocument()
    expect(screen.queryByText('UnderReview')).toBeNull()
  })

  it("leaves an offering's supplier code alone, because a code is not a word", async () => {
    // The control. Without it the two tests above would pass just as well against a screen that had
    // started translating identifiers, which is the opposite mistake and a worse one: a reference
    // code a person cannot copy is a reference code that does not work.
    restore = mockFetch({
      '/api/v1/search': {
        query: 'valves',
        hits: [hit({ kind: 'offering', referenceCode: null, titleEn: 'Industrial valves', context: 'SUP-000001' })],
        truncated: false,
      },
    })
    routeQuery.current = 'valves'

    renderPage(<SearchPage />)

    expect(await screen.findByText('SUP-000001')).toBeInTheDocument()
  })

  it('says what it searches before anything has been searched', () => {
    // The screen was one empty box and nothing else, which reads as unfinished rather than as a
    // starting point - and it was the destination of a top-bar control that LOOKED like a search box,
    // so a reader arrived having already typed once.
    restore = mockFetch({ '/api/v1/search': { query: '', hits: [], truncated: false } })

    renderPage(<SearchPage />)

    expect(screen.getByText(/Nothing searched yet/i)).toBeInTheDocument()
  })

  it('puts the submitted query in the address rather than in component state', () => {
    restore = mockFetch({ '/api/v1/search': { query: 'catering', hits: [hit()], truncated: false } })
    routeQuery.current = 'catering'

    renderPage(<SearchPage />)

    // Arriving with a query fills the box with it, so the reader can see and edit what was searched
    // instead of facing an empty field above their own results.
    expect(screen.getByLabelText(/search|بحث/i)).toHaveValue('catering')
  })

  it('does not query until the form is submitted', async () => {
    const requests: RecordedRequest[] = []
    restore = mockFetch({ '/api/v1/search': { query: 'catering', hits: [hit()], truncated: false } }, requests)

    renderPage(<SearchPage />)

    // Typing must not fire anything. A keystroke-per-request search over three tables is a load test
    // aimed at the database, which is the reason this page holds a draft separate from the submitted term.
    await userEvent.type(screen.getByLabelText(/search|بحث/i), 'catering')
    expect(requests).toHaveLength(0)

    await userEvent.click(screen.getByRole('button', { name: /search|بحث/i }))
    // The submit is a navigation now. Nothing is fetched until the address carries the query, which is
    // what makes the search linkable and what lets the top bar submit into this screen.
    expect(navigated).toEqual([{ to: '/back-office/search', search: { q: 'catering' } }])
    expect(requests.filter((r) => r.url.includes('/api/v1/search'))).toHaveLength(0)

    routeQuery.current = 'catering'
    renderPage(<SearchPage />)
    expect(await screen.findByText('Catering RFQ')).toBeInTheDocument()
  })

  it('says so when the cap cut the results off', async () => {
    // A search that MISSED the row must not look like a search that found nothing - that is the whole
    // reason the server sends `truncated` rather than just a shorter list.
    restore = mockFetch({ '/api/v1/search': { query: 'a', hits: [hit()], truncated: true } })

    routeQuery.current = 'a'
    renderPage(<SearchPage />)

    expect(await screen.findByRole('status')).toBeInTheDocument()
  })

  it('renders an offering as plain text, because it has no page to link to', async () => {
    restore = mockFetch({
      '/api/v1/search': {
        query: 'valves',
        hits: [hit({ kind: 'offering', referenceCode: null, titleEn: 'Industrial valves', context: 'SUP-000001' })],
        truncated: false,
      },
    })

    routeQuery.current = 'valves'
    renderPage(<SearchPage />)

    const title = await screen.findByText('Industrial valves')
    // The control: an RFQ hit in the same shape IS a link, so this assertion is about the offering
    // rule and not about the test failing to find links at all.
    expect(title.closest('a')).toBeNull()
    expect(screen.getByText('SUP-000001')).toBeInTheDocument()
  })

  it('links an RFQ hit to its detail page', async () => {
    restore = mockFetch({ '/api/v1/search': { query: 'catering', hits: [hit()], truncated: false } })

    routeQuery.current = 'catering'
    renderPage(<SearchPage />)

    const title = await screen.findByText('Catering RFQ')
    expect(title.closest('a')).toHaveAttribute('to', '/back-office/rfqs/RFQ-2026-000006')
  })

  it('shows the unstemmed-search caveat when nothing matched', async () => {
    restore = mockFetch({ '/api/v1/search': { query: 'valve', hits: [], truncated: false } })

    routeQuery.current = 'valve'
    renderPage(<SearchPage />)

    // Whole words and prefixes only: a plural will not find a singular, and a person who searched once
    // and got nothing needs to be told that here rather than in a document nobody reads.
    expect(await screen.findByText(/a plural will not find a singular|فصيغة الجمع لا تجد المفرد/i))
      .toBeInTheDocument()
  })

  it('offers a retry instead of a blank page when the search fails', async () => {
    restore = mockFetch({ '/api/v1/search': { __status: 500 } })

    routeQuery.current = 'catering'
    renderPage(<SearchPage />)

    expect(await screen.findByRole('button', { name: /try again|إعادة المحاولة/i })).toBeInTheDocument()
  })
})
