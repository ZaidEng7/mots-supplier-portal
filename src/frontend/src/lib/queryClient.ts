// The React Query client, and the one helper that fires a cache invalidation without awaiting it.
//
// Task #19: QueryClient.invalidateQueries returns a Promise that rejects if the resulting refetch fails, so awaiting it
// unhandled is a floating promise - oxlint-tsgolint's no-floating-promises - and every call site in this codebase fires it
// from inside a mutation's own onSuccess, after the user has already seen a success toast for the thing that actually
// mattered.
//
// A second, contradictory "failed" toast for a background cache refresh would be confusing rather than helpful: the stale
// cache self-heals on the next refetch, through React Query's own retry and refocus behaviour. Logging rather than surfacing
// is the deliberate choice, because silently swallowing it entirely would hide a real failure from anyone debugging stale UI
// later.

import { QueryClient, type InvalidateQueryFilters } from '@tanstack/react-query'

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      retry: 1,
    },
  },
})

export function invalidateQuietly(client: QueryClient, filters: InvalidateQueryFilters): void {
  client.invalidateQueries(filters).catch((error: unknown) => {
    console.error('Background query invalidation failed', filters, error)
  })
}
