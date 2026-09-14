// T-077 with SCR-701 and SCR-702: the platform's own staff accounts, as an administrator sees and administers
// them.
//
// mfaEnabled and activeSessionCount are the two facts that make a row actionable rather than decorative: a
// system_admin who lost their authenticator is a real lockout, and a deactivation that left sessions alive would
// only stop the NEXT sign-in. The buying body is null for ministry_viewer and system_admin, which have none by
// design.
//
// acceptStaffInvite sends no auth header, because the invitee has no session yet - the same reasoning as
// acceptTeamInvite in api/team.ts.
//
// listStaff is keyset-paged on (email, id), like every other list here.
//
// resetStaffMfa clears the authenticator enrolment and every live session. It is an administrator action on
// someone ELSE's account: a self-service reset would be a way past the second factor.

import { apiFetch } from './auth'
import { SupplierApiError } from './supplier'

export interface Staff {
  userId: string
  email: string
  fullName: string
  role: string
}

export interface StaffAccount {
  userId: string
  email: string
  fullName: string
  role: string | null
  isActive: boolean
  mfaEnabled: boolean
  lockoutEnd: string | null
  activeSessionCount: number
}

export interface InviteStaffPayload {
  email: string
  fullName: string
  role: string
  organizationId?: string | null
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

export async function listStaff(cursor?: string): Promise<{ data: StaffAccount[]; pagination: { hasMore: boolean; nextCursor: string | null } }> {
  const query = cursor ? `?cursor=${encodeURIComponent(cursor)}` : ''
  return parseOrThrow(await apiFetch(`/api/v1/staff${query}`))
}

export async function setStaffActive(userId: string, isActive: boolean): Promise<StaffAccount> {
  const action = isActive ? 'reactivate' : 'deactivate'
  return parseOrThrow(await apiFetch(`/api/v1/staff/${userId}/${action}`, { method: 'POST' }))
}

export async function changeStaffRole(userId: string, role: string): Promise<StaffAccount> {
  return parseOrThrow(await apiFetch(`/api/v1/staff/${userId}/role`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ role }),
  }))
}

export async function resetStaffMfa(userId: string): Promise<StaffAccount> {
  return parseOrThrow(await apiFetch(`/api/v1/staff/${userId}/reset-mfa`, { method: 'POST' }))
}
