import type { ReactElement, ReactNode } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { I18nextProvider } from 'react-i18next'
import { render } from '@testing-library/react'
import i18n from '../i18n/config'
import { ToastProvider } from '../components/ui'

/**
 * Renders a page component with the providers it needs, so page-level behaviour can be asserted.
 *
 * <p><b>Why this exists.</b> The frontend had no page-level testing, which meant a UI change could
 * only be verified by opening a browser. That is not a hypothetical gap: the reviewer profile grid
 * crashed for every supplier and no test could have caught it, and the lifecycle dialog carried a
 * stale reason between actions - a mechanism for writing a false justification into an append-only
 * audit log - which was found by clicking, not by CI.</p>
 *
 * <p><b>Deliberately small.</b> Providers and fetch mocking, nothing else. It is not a testing
 * framework: no custom matchers, no page objects, no re-export of the whole of Testing Library.
 * Tests import what they need from @testing-library/react directly, so this file has one job and
 * stays easy to delete or replace.</p>
 *
 * <p>The router is NOT included. Pages that read route params take them as props or via a mocked
 * hook in the test that needs it; wiring a real router here would mean every page test depends on
 * the whole route tree, which is how a small harness becomes a large one.</p>
 */

/**
 * A QueryClient per test. Sharing the app's singleton would leak cached data between tests, so a
 * failure in one could be caused by another - and retries are off so a deliberately failing request
 * fails once rather than after several seconds of silence.
 */
export function createTestQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: false, staleTime: 0, gcTime: 0 },
      mutations: { retry: false },
    },
  })
}

/**
 * Return type is inferred rather than declared. An explicit `extends RenderResult` looked tidier
 * and did not match what render() actually returns - tsc caught it, the tests did not, which is
 * the same reason `npm run build` and not `vitest` is the real gate here.
 */
export function renderPage(ui: ReactElement) {
  const queryClient = createTestQueryClient()

  function Providers({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>
        <I18nextProvider i18n={i18n}>
          <ToastProvider>{children}</ToastProvider>
        </I18nextProvider>
      </QueryClientProvider>
    )
  }

  return { ...render(ui, { wrapper: Providers }), queryClient }
}

/**
 * Replaces global fetch for one test.
 *
 * Routes are matched by substring on the URL, so a test states only the calls it cares about. An
 * unmatched request THROWS rather than returning a default: a page quietly rendering an empty state
 * because a request nobody declared returned undefined is the kind of green test that asserts
 * nothing, which is the pattern this project keeps finding.
 *
 * Returns a restore function; call it in afterEach.
 */
export function mockFetch(routes: Record<string, unknown>): () => void {
  const original = globalThis.fetch

  globalThis.fetch = (async (input: RequestInfo | URL) => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
    // LONGEST match, not first declared. §12-A/C2 introduced nested routes where one declared
    // path is a strict prefix of another - "/api/v1/rfqs/RFQ-1" and "/api/v1/rfqs/RFQ-1/proposals"
    // - and a first-match-wins substring search answers the sub-route with the parent's fixture.
    // That is silent: the page renders the wrong shape rather than failing, which cost real time
    // to diagnose. Sorting by specificity makes declaration order irrelevant.
    const match = Object.keys(routes)
      .filter((route) => url.includes(route))
      .sort((a, b) => b.length - a.length)[0]

    if (match === undefined) {
      throw new Error(
        `No mock declared for ${url}. Declare it in mockFetch, or the test is asserting against ` +
          'a page whose data never arrived.',
      )
    }

    const body = routes[match]

    // A fixture of the shape { __status: 4xx|5xx } declares a FAILING response for that route.
    // Added because every screen's error state needs one and the alternative - leaving the route
    // undeclared so the harness throws - reads as a missing mock to the next person, which is the
    // opposite of what the test means.
    if (body !== null && typeof body === 'object' && '__status' in body) {
      const status = (body as { __status: number }).__status
      return new Response(JSON.stringify({ status }), {
        status,
        headers: { 'Content-Type': 'application/json' },
      })
    }

    return new Response(JSON.stringify(body), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })
  }) as typeof fetch

  return () => {
    globalThis.fetch = original
  }
}

/**
 * Builds the documented §5.2 list envelope around `items` for a `mockFetch` route.
 *
 * <p>Every list endpoint returns `{ data, pagination, meta }`, and `useInfiniteQuery` reads
 * `pagination.hasMore` before it renders anything - so a mock that returns a bare array, or the
 * old flat `{ items, hasMore, nextCursor }`, crashes the page rather than failing an assertion.
 * One builder so a new list test cannot reintroduce a hand-written envelope that drifts from the
 * real one.</p>
 */
export function listPage<T>(items: T[], overrides: { hasMore?: boolean; nextCursor?: string | null } = {}) {
  return {
    data: items,
    pagination: {
      mode: 'cursor' as const,
      nextCursor: overrides.nextCursor ?? null,
      prevCursor: null,
      pageSize: 20,
      totalCount: null,
      hasMore: overrides.hasMore ?? false,
    },
    meta: { sort: null, filtersApplied: null },
  }
}
