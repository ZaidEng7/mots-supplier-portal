import { apiFetch } from './auth'
import type { SupplierProfile } from './supplier'
import { SupplierApiError } from './supplier'

async function parseOrThrow<T>(res: Response): Promise<T> {
  const text = await res.text()
  const body = text ? JSON.parse(text) : null
  if (!res.ok) throw new SupplierApiError(res.status, body)
  return body as T
}

export async function linkCategory(categoryCode: string): Promise<SupplierProfile> {
  const res = await apiFetch('/api/v1/suppliers/me/category-links', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ categoryCode }),
  })
  return parseOrThrow(res)
}

export async function unlinkCategory(categoryCode: string): Promise<SupplierProfile> {
  const res = await apiFetch(`/api/v1/suppliers/me/category-links/${encodeURIComponent(categoryCode)}`, { method: 'DELETE' })
  return parseOrThrow(res)
}
