// A supplier's authorised representatives, including which one is primary.
//
// Every call returns the whole SupplierProfile rather than the representative it changed, because the profile is
// the aggregate and its version moves with every child write - so the caller gets the fresh state and the
// transport gets the fresh ETag. Setting the primary is a POST to the representative rather than a field on an
// update, because it moves a flag off whichever representative held it, which is a change to the aggregate and
// not to one row.

import { apiFetch } from './auth'
import type { SupplierProfile } from './supplier'
import { SupplierApiError } from './supplier'

export interface RepresentativePayload {
  fullName: string
  email: string
  phone?: string | null
  position?: string | null
}

async function parseOrThrow<T>(res: Response): Promise<T> {
  const text = await res.text()
  const body = text ? JSON.parse(text) : null
  if (!res.ok) throw new SupplierApiError(res.status, body)
  return body as T
}

export async function addRepresentative(payload: RepresentativePayload): Promise<SupplierProfile> {
  const res = await apiFetch('/api/v1/suppliers/me/representatives', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })
  return parseOrThrow(res)
}

export async function updateRepresentative(id: string, payload: RepresentativePayload): Promise<SupplierProfile> {
  const res = await apiFetch(`/api/v1/suppliers/me/representatives/${id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })
  return parseOrThrow(res)
}

export async function removeRepresentative(id: string): Promise<SupplierProfile> {
  const res = await apiFetch(`/api/v1/suppliers/me/representatives/${id}`, { method: 'DELETE' })
  return parseOrThrow(res)
}

export async function setPrimaryRepresentative(id: string): Promise<SupplierProfile> {
  const res = await apiFetch(`/api/v1/suppliers/me/representatives/${id}/set-primary`, { method: 'POST' })
  return parseOrThrow(res)
}
