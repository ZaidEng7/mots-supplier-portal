import { apiFetch } from './auth'
import { SupplierApiError } from './supplier'

export interface Staff {
  userId: string
  email: string
  fullName: string
  role: string
}

export interface InviteStaffPayload {
  email: string
  fullName: string
  role: string
}

async function parseOrThrow<T>(res: Response): Promise<T> {
  const text = await res.text()
  const body = text ? JSON.parse(text) : null
  if (!res.ok) throw new SupplierApiError(res.status, body)
  return body as T
}

export async function inviteStaff(payload: InviteStaffPayload): Promise<Staff> {
  const res = await apiFetch('/api/v1/staff/invite', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })
  return parseOrThrow(res)
}

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5080'

/** No auth header - the invitee has no session yet, same as acceptTeamInvite (api/team.ts). */
export async function acceptStaffInvite(token: string, password: string): Promise<void> {
  const res = await fetch(`${API_BASE_URL}/api/v1/staff/accept-invite`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ token, password }),
  })
  if (!res.ok) {
    const text = await res.text()
    throw new SupplierApiError(res.status, text ? JSON.parse(text) : null)
  }
}
