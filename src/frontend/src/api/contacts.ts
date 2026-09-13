// A supplier's contact people.
//
// Every call returns the whole SupplierProfile rather than the contact it changed, because the profile is the
// aggregate and its version moves with every child write - so the caller gets the fresh state and the transport
// gets the fresh ETag. They throw SupplierApiError for the same reason: to a screen, a refused contact edit is
// the profile refusing a write.

import { apiFetch } from './auth'
import type { SupplierProfile } from './supplier'
import { SupplierApiError } from './supplier'

export interface ContactPayload {
  fullName: string
  email: string
  phone?: string | null
  role?: string | null
}

async function parseOrThrow<T>(res: Response): Promise<T> {
  const text = await res.text()
  const body = text ? JSON.parse(text) : null
  if (!res.ok) throw new SupplierApiError(res.status, body)
  return body as T
}

export async function addContact(payload: ContactPayload): Promise<SupplierProfile> {
  const res = await apiFetch('/api/v1/suppliers/me/contacts', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })
  return parseOrThrow(res)
}

export async function updateContact(id: string, payload: ContactPayload): Promise<SupplierProfile> {
  const res = await apiFetch(`/api/v1/suppliers/me/contacts/${id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })
  return parseOrThrow(res)
}

export async function removeContact(id: string): Promise<SupplierProfile> {
  const res = await apiFetch(`/api/v1/suppliers/me/contacts/${id}`, { method: 'DELETE' })
  return parseOrThrow(res)
}
