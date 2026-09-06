import { apiFetch } from './auth'

/** EPIC-20/SCR-906. */
export interface SearchHit {
  /** "rfq" | "supplier" | "offering" — a string, not a union, because a new searchable entity is an
   *  additive wire change and a union here would make the client the thing that has to be redeployed. */
  kind: string
  /** Null for an offering, which has no public code of its own; its supplier's code is in `context`. */
  referenceCode: string | null
  titleAr: string
  titleEn: string
  context: string | null
  rank: number
}

export interface SearchResults {
  query: string
  hits: SearchHit[]
  /** True when the cap cut results off. Shown, because a search that missed the row must not look like a
   *  search that found nothing. */
  truncated: boolean
}

export async function search(query: string): Promise<SearchResults> {
  const response = await apiFetch(`/api/v1/search?q=${encodeURIComponent(query)}`)
  if (!response.ok) throw new Error('search_failed')
  return (await response.json()) as SearchResults
}
