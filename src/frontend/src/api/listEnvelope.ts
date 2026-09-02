/**
 * The documented list envelope, API-ARCHITECTURE.md §5.2:
 * `{ data, pagination: { mode, nextCursor, prevCursor, pageSize, totalCount, hasMore }, meta: { sort, filtersApplied } }`
 *
 * <p>Replaces the three separate `Page<T>` interfaces that `api/team.ts`, `api/review.ts` and
 * `api/settings.ts` each declared for themselves against the old backend `Page<T>`
 * (`items`/`hasMore`/`nextCursor`). One definition, so the next list endpoint cannot invent a
 * fourth.</p>
 *
 * <p>`totalCount` is null unless the caller asked for it (`?withCount=true`), and `prevCursor` is
 * always null today - no endpoint supports backward paging. Both are typed as present-but-nullable
 * rather than optional, because the backend always emits the keys.</p>
 */
export interface ListEnvelope<T> {
  data: T[]
  pagination: {
    mode: 'cursor' | 'page'
    nextCursor: string | null
    prevCursor: string | null
    pageSize: number
    totalCount: number | null
    hasMore: boolean
  }
  meta: {
    sort: string | null
    filtersApplied: string[] | null
  }
}

/**
 * `getNextPageParam` for TanStack Query's `useInfiniteQuery`, shared by every paginated list so the
 * envelope's shape is read in exactly one place.
 */
export function nextPageParam<T>(lastPage: ListEnvelope<T>): string | undefined {
  return lastPage.pagination.hasMore ? lastPage.pagination.nextCursor ?? undefined : undefined
}
