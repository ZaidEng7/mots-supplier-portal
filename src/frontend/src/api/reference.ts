// The reference catalogues a form needs: currencies, regions, categories, units of measure and Incoterms. Plus
// the readiness check behind the system banner.
//
// The Incoterms list is T-072's: the delivery terms a bid may quote - Incoterms 2020, minus whatever the ministry
// has deactivated. It is the same list the server validates a submitted bid against, so a term offered here
// cannot be refused on save.
//
// fetchHealth calls /health/ready. The combined /health endpoint was split into /health/live and /health/ready
// under Task #16 and NFR-OBS-006, and this call was never updated, so it hit a route that no longer exists and
// fell through to the deny-by-default auth fallback - a 401 regardless of actual backend health. /health/ready is
// the meaningful one for an "is the system usable" banner: it also checks Postgres, pending migrations, object
// storage and Hangfire storage, rather than only process-alive.

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5080'

export interface Currency {
  id: string
  code: string
  nameAr: string
  nameEn: string
}

export async function fetchCurrencies(): Promise<Currency[]> {
  const res = await fetch(`${API_BASE_URL}/api/v1/reference/currencies`)
  if (!res.ok) throw new Error(`Failed to fetch currencies: ${res.status}`)
  return res.json()
}

export interface Region {
  id: string
  code: string
  nameAr: string
  nameEn: string
}

export async function fetchRegions(): Promise<Region[]> {
  const res = await fetch(`${API_BASE_URL}/api/v1/reference/regions`)
  if (!res.ok) throw new Error(`Failed to fetch regions: ${res.status}`)
  return res.json()
}

export interface Category {
  id: string
  code: string
  nameAr: string
  nameEn: string
}

export async function fetchCategories(): Promise<Category[]> {
  const res = await fetch(`${API_BASE_URL}/api/v1/reference/categories`)
  if (!res.ok) throw new Error(`Failed to fetch categories: ${res.status}`)
  return res.json()
}

export interface UnitOfMeasure {
  id: string
  code: string
  nameAr: string
  nameEn: string
}

export async function fetchUnitsOfMeasure(): Promise<UnitOfMeasure[]> {
  const res = await fetch(`${API_BASE_URL}/api/v1/reference/units-of-measure`)
  if (!res.ok) throw new Error(`Failed to fetch units of measure: ${res.status}`)
  return res.json()
}

export interface Incoterm {
  id: string
  code: string
  nameAr: string
  nameEn: string
}

export async function fetchIncoterms(): Promise<Incoterm[]> {
  const res = await fetch(`${API_BASE_URL}/api/v1/reference/incoterms`)
  if (!res.ok) throw new Error(`Failed to fetch incoterms: ${res.status}`)
  return res.json()
}

export async function fetchHealth(): Promise<string> {
  const res = await fetch(`${API_BASE_URL}/health/ready`)
  if (!res.ok) throw new Error(`Health check failed: ${res.status}`)
  return res.text()
}
