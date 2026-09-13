// The documented list envelope, API-ARCHITECTURE.md §5.2:
// { data, pagination: { mode, nextCursor, prevCursor, pageSize, totalCount, hasMore }, meta: { sort,
// filtersApplied } }.
//
// It replaces the three separate Page<T> interfaces that api/team.ts, api/review.ts and api/settings.ts each
// declared for themselves against the old backend Page<T> shape of items, hasMore and nextCursor. One
// definition, so the next list endpoint cannot invent a fourth.
//
// totalCount is null unless the caller asked for it with ?withCount=true, and prevCursor is always null today,
// because no endpoint supports backward paging. Both are typed as present-but-nullable rather than optional,
// because the backend always emits the keys.

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

export function nextPageParam<T>(lastPage: ListEnvelope<T>): string | undefined {
  return lastPage.pagination.hasMore ? lastPage.pagination.nextCursor ?? undefined : undefined
}
