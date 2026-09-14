// A supplier administering their own team: who is on it, inviting someone, and disabling them.
//
// The list is cursor-paged. Disabling is a POST rather than a DELETE, because the account is the actor on audit
// rows and deactivation is not deletion.
//
// acceptTeamInvite is the one call here that does NOT go through apiFetch, and it has its own base URL for that
// reason: it is how someone with no session at all sets their password from an invitation token, so attaching a
// token it does not have would be meaningless and the expiry overlay must not fire over a public page.

import { apiFetch } from './auth'
import type { ListEnvelope } from './listEnvelope'
import { SupplierApiError } from './supplier'

export interface TeamMember {
  userId: string
  email: string
  fullName: string
  isActive: boolean
}

export interface InvitePayload {
  email: string
  fullName: string
}


async function parseOrThrow<T>(res: Response): Promise<T> {
  const text = await res.text()
  const body = text ? JSON.parse(text) : null
  if (!res.ok) throw new SupplierApiError(res.status, body)
  return body as T
}

export async function listTeam(cursor?: string | null): Promise<ListEnvelope<TeamMember>> {
  const qs = cursor ? `?cursor=${encodeURIComponent(cursor)}` : ''
  const res = await apiFetch(`/api/v1/suppliers/me/users${qs}`)
  return parseOrThrow(res)
}

export async function inviteTeamMember(payload: InvitePayload): Promise<TeamMember> {
  const res = await apiFetch('/api/v1/suppliers/me/users', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })
  return parseOrThrow(res)
}

export async function disableTeamMember(userId: string): Promise<void> {
  const res = await apiFetch(`/api/v1/suppliers/me/users/${userId}/disable`, { method: 'POST' })
  if (!res.ok) {
    const text = await res.text()
    throw new SupplierApiError(res.status, text ? JSON.parse(text) : null)
  }
}

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5080'

export async function acceptTeamInvite(token: string, password: string): Promise<void> {
  const res = await fetch(`${API_BASE_URL}/api/v1/supplier-users/accept-invite`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ token, password }),
  })
  if (!res.ok) {
    const text = await res.text()
    throw new SupplierApiError(res.status, text ? JSON.parse(text) : null)
  }
}
