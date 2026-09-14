// EPIC-20 and SCR-906: the cross-entity search.
//
// A hit's kind is "rfq", "supplier" or "offering" - typed as a string rather than a union, because a new
// searchable entity is an additive wire change and a union here would make the client the thing that has to be
// redeployed. The public code is null for an offering, which has none of its own; its supplier's code is in the
// context instead.
//
// The results say when the cap cut them off, and that is shown, because a search that missed the row must not
// look like a search that found nothing.

import { apiFetch } from './auth'

export interface SearchHit {
  kind: string
  referenceCode: string | null
  titleAr: string
  titleEn: string
  context: string | null
  rank: number
}

export interface SearchResults {
  query: string
  hits: SearchHit[]
  truncated: boolean
}

export async function search(query: string): Promise<SearchResults> {
  const response = await apiFetch(`/api/v1/search?q=${encodeURIComponent(query)}`)
  if (!response.ok) throw new Error('search_failed')
  return (await response.json()) as SearchResults
}
