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

/**
 * The single-offering read, which exists to ISSUE the precondition the two writes below require.
 *
 * <p>`PUT /offerings/{id}` and `POST /offerings/{id}/deactivate` both declare RequireIfMatch, and the
 * only route that emits an offering's ETag is this one - the LIST does not, and could not usefully:
 * a collection's version is not any item's version, so filing one under the collection path would
 * produce a 412 rather than a 428. Nothing called this, so every edit and every deactivation from
 * the catalogue answered 428 and nothing saved.</p>
 *
 * <p>This is the batch-3 Offering lesson a second time, from the other side: back then the guard
 * arrived without a route that could satisfy it, and the item GET was added to fix that. The route
 * has been there ever since; the client just never used it.</p>
 */
export async function getOffering(offeringId: string): Promise<Offering> {
  const res = await apiFetch(`/api/v1/suppliers/me/offerings/${offeringId}`)
  return parseOrThrow(res)
}

export async function updateOffering(offeringId: string, payload: OfferingPayload): Promise<Offering> {
  // Read first, for the version. apiFetch files this read's ETag under the offering's own path, which
  // is the prefix the write below walks up to.
  await getOffering(offeringId)
  const res = await apiFetch(`/api/v1/suppliers/me/offerings/${offeringId}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })
  return parseOrThrow(res)
}

export async function deactivateOffering(offeringId: string): Promise<Offering> {
  await getOffering(offeringId)
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
