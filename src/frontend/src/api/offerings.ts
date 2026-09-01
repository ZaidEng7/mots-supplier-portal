import { apiFetch } from './auth'
import { SupplierApiError } from './supplier'

export interface Offering {
  id: string
  nameAr: string
  nameEn: string
  description: string | null
  categoryCode: string
  unitOfMeasureCode: string
  priceAmount: number | null
  currencyCode: string | null
  isActive: boolean
  attributes: Record<string, string> | null
}

export interface OfferingPayload {
  nameAr: string
  nameEn: string
  description: string | null
  categoryCode: string
  unitOfMeasureCode: string
  priceAmount: number | null
  currencyCode: string | null
  attributes: Record<string, string> | null
}

/** FEAT-06.3/FR-OFF-004: a buyer-search result, distinct from Offering above - it carries the
 * owning supplier's identity (never exposed in the supplier's own CRUD view) and is already
 * lifecycle-filtered server-side (FEAT-06.4), so nothing here needs an isActive flag. */
export interface BuyerOfferingSearchResult {
  id: string
  supplierReferenceCode: string
  supplierDisplayNameAr: string
  supplierDisplayNameEn: string
  nameAr: string
  nameEn: string
  description: string | null
  categoryCode: string
  unitOfMeasureCode: string
  priceAmount: number | null
  currencyCode: string | null
  attributes: Record<string, string> | null
}

async function parseOrThrow<T>(res: Response): Promise<T> {
  const text = await res.text()
  const body = text ? JSON.parse(text) : null
  if (!res.ok) throw new SupplierApiError(res.status, body)
  return body as T
}

export async function listOfferings(): Promise<Offering[]> {
  const res = await apiFetch('/api/v1/suppliers/me/offerings')
  return parseOrThrow(res)
}

export async function createOffering(payload: OfferingPayload): Promise<Offering> {
  const res = await apiFetch('/api/v1/suppliers/me/offerings', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })
  return parseOrThrow(res)
}

export async function updateOffering(offeringId: string, payload: OfferingPayload): Promise<Offering> {
  const res = await apiFetch(`/api/v1/suppliers/me/offerings/${offeringId}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })
  return parseOrThrow(res)
}

export async function deactivateOffering(offeringId: string): Promise<Offering> {
  const res = await apiFetch(`/api/v1/suppliers/me/offerings/${offeringId}/deactivate`, { method: 'POST' })
  return parseOrThrow(res)
}

export async function searchBuyerOfferings(filters: { categoryCode?: string; query?: string }): Promise<BuyerOfferingSearchResult[]> {
  const params = new URLSearchParams()
  if (filters.categoryCode) params.set('categoryCode', filters.categoryCode)
  if (filters.query) params.set('query', filters.query)
  const qs = params.toString()
  const res = await apiFetch(`/api/v1/offerings/search${qs ? `?${qs}` : ''}`)
  return parseOrThrow(res)
}
