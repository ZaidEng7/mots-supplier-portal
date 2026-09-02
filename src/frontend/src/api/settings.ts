import { apiFetch } from './auth'
import type { ListEnvelope } from './listEnvelope'

export interface EnrollMfaResponse {
  sharedKey: string
  authenticatorUri: string
}

export interface ConfirmMfaResponse {
  enrolled: boolean
  recoveryCodes: string[]
}

export class SettingsApiError extends Error {
  status: number
  constructor(status: number, body: unknown) {
    const b = body as { error?: string } | null
    super(b?.error ?? `Request failed: ${status}`)
    this.status = status
  }
}

async function parseOrThrow<T>(res: Response): Promise<T> {
  const text = await res.text()
  const body = text ? JSON.parse(text) : null
  if (!res.ok) throw new SettingsApiError(res.status, body)
  return body as T
}

export async function enrollMfa(): Promise<EnrollMfaResponse> {
  const res = await apiFetch('/api/v1/auth/mfa/enroll', { method: 'POST' })
  return parseOrThrow(res)
}

export async function confirmMfaEnrollment(code: string): Promise<ConfirmMfaResponse> {
  const res = await apiFetch('/api/v1/auth/mfa/confirm', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ code }),
  })
  return parseOrThrow(res)
}

export interface Session {
  familyId: string
  ip: string | null
  userAgent: string | null
  createdAt: string
  expiresAt: string
  isCurrent: boolean
}


export async function listSessions(cursor?: string | null): Promise<ListEnvelope<Session>> {
  const qs = cursor ? `?cursor=${encodeURIComponent(cursor)}` : ''
  const res = await apiFetch(`/api/v1/auth/sessions${qs}`)
  return parseOrThrow(res)
}

export async function revokeSession(familyId: string): Promise<void> {
  const res = await apiFetch(`/api/v1/auth/sessions/${familyId}/revoke`, { method: 'POST' })
  if (!res.ok) throw new SettingsApiError(res.status, null)
}

export async function revokeAllOtherSessions(): Promise<{ revokedCount: number }> {
  const res = await apiFetch('/api/v1/auth/sessions/revoke-all', { method: 'POST' })
  return parseOrThrow(res)
}
