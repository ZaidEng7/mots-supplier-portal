import { apiFetch } from './auth'
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

/** MSP-84: matches backend Application/Common/Page.cs - keyset-paged, not offset. */
export interface Page<T> {
  items: T[]
  hasMore: boolean
  nextCursor: string | null
}

async function parseOrThrow<T>(res: Response): Promise<T> {
  const text = await res.text()
  const body = text ? JSON.parse(text) : null
  if (!res.ok) throw new SupplierApiError(res.status, body)
  return body as T
}

export async function listTeam(cursor?: string | null): Promise<Page<TeamMember>> {
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
