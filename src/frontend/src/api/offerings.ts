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
}

export interface OfferingPayload {
  nameAr: string
  nameEn: string
  description: string | null
  categoryCode: string
  unitOfMeasureCode: string
  priceAmount: number | null
  currencyCode: string | null
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
