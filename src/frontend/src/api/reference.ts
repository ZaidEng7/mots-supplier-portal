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

export async function fetchHealth(): Promise<string> {
  // The combined /health endpoint was split into /health/live and /health/ready (Task #16 /
  // NFR-OBS-006) - this call was never updated, so it hit a route that no longer exists and fell
  // through to the deny-by-default auth fallback (401) regardless of actual backend health.
  // /health/ready is the meaningful one for a "is the system usable" banner: it also checks
  // Postgres, pending migrations, object storage, and Hangfire storage, not just process-alive.
  const res = await fetch(`${API_BASE_URL}/health/ready`)
  if (!res.ok) throw new Error(`Health check failed: ${res.status}`)
  return res.text()
}
