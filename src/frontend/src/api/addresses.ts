import { apiFetch } from './auth'
import type { SupplierProfile } from './supplier'
import { SupplierApiError } from './supplier'

export interface AddressPayload {
  kind: string
  line1: string
  line2?: string | null
  city: string
  regionCode: string
  country: string
  postalCode?: string | null
  latitude?: number | null
  longitude?: number | null
}

export interface BranchPayload {
  nameAr: string
  nameEn: string
  addressId?: string | null
  isActive?: boolean
}

async function parseOrThrow<T>(res: Response): Promise<T> {
  const text = await res.text()
  const body = text ? JSON.parse(text) : null
  if (!res.ok) throw new SupplierApiError(res.status, body)
  return body as T
}

export async function addAddress(payload: AddressPayload): Promise<SupplierProfile> {
  const res = await apiFetch('/api/v1/suppliers/me/addresses', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })
  return parseOrThrow(res)
}

export async function updateAddress(id: string, payload: AddressPayload): Promise<SupplierProfile> {
  const res = await apiFetch(`/api/v1/suppliers/me/addresses/${id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })
  return parseOrThrow(res)
}

export async function removeAddress(id: string): Promise<SupplierProfile> {
  const res = await apiFetch(`/api/v1/suppliers/me/addresses/${id}`, { method: 'DELETE' })
  return parseOrThrow(res)
}

export async function addBranch(payload: BranchPayload): Promise<SupplierProfile> {
  const res = await apiFetch('/api/v1/suppliers/me/branches', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })
  return parseOrThrow(res)
}

export async function updateBranch(id: string, payload: Required<BranchPayload>): Promise<SupplierProfile> {
  const res = await apiFetch(`/api/v1/suppliers/me/branches/${id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })
  return parseOrThrow(res)
}

export async function removeBranch(id: string): Promise<SupplierProfile> {
  const res = await apiFetch(`/api/v1/suppliers/me/branches/${id}`, { method: 'DELETE' })
  return parseOrThrow(res)
}
