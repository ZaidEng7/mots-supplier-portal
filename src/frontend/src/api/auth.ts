import { useAuthStore } from '../lib/authStore'

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5080'

export interface TokenResponse {
  accessToken: string
  accessTokenExpiresAt: string
}

export class ApiError extends Error {
  status: number
  body: unknown

  constructor(status: number, body: unknown) {
    super(typeof body === 'object' && body && 'error' in body ? String((body as { error: unknown }).error) : `Request failed: ${status}`)
    this.status = status
    this.body = body
  }
}

async function parseJsonOrThrow<T>(res: Response): Promise<T> {
  const text = await res.text()
  const body = text ? JSON.parse(text) : null
  if (!res.ok) throw new ApiError(res.status, body)
  return body as T
}

/** totpCode is omitted on the first call; if the account has MFA enabled the API answers
 * 401 { error: 'mfa_required' } and the same credentials are re-posted with the code the user
 * enters next (Api/Endpoints/AuthEndpoints.cs `/login` - no separate verify endpoint exists,
 * the login endpoint itself is the seam). */
export async function login(email: string, password: string, totpCode?: string): Promise<TokenResponse> {
  const res = await fetch(`${API_BASE_URL}/api/v1/auth/login`, {
    method: 'POST',
    credentials: 'include',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password, totpCode }),
  })
  return parseJsonOrThrow<TokenResponse>(res)
}

/** Treats an unreachable API the same as "not authenticated" - a network failure here must not
 * hang the caller (e.g. the router's auth guard) forever waiting on an uncaught rejection. */
export async function refresh(): Promise<TokenResponse | null> {
  try {
    const res = await fetch(`${API_BASE_URL}/api/v1/auth/refresh`, {
      method: 'POST',
      credentials: 'include',
    })
    if (!res.ok) return null
    return await res.json()
  } catch {
    return null
  }
}

export async function logout(): Promise<void> {
  await fetch(`${API_BASE_URL}/api/v1/auth/logout`, { method: 'POST', credentials: 'include' })
}

export async function forgotPassword(email: string): Promise<void> {
  const res = await fetch(`${API_BASE_URL}/api/v1/auth/forgot-password`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email }),
  })
  await parseJsonOrThrow(res)
}

export async function resetPassword(token: string, newPassword: string): Promise<void> {
  const res = await fetch(`${API_BASE_URL}/api/v1/auth/reset-password`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ token, newPassword }),
  })
  await parseJsonOrThrow(res)
}

export async function resendVerification(email: string): Promise<void> {
  const res = await fetch(`${API_BASE_URL}/api/v1/registrations/resend-verification`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email }),
  })
  await parseJsonOrThrow(res)
}

export interface RegisterSupplierPayload {
  displayNameAr: string
  displayNameEn: string
  registrationNumber?: string
  representativeName: string
  representativePhone: string
  email: string
  password: string
}

/** MSP-73: referenceCode is null when the email/registration number was already taken - the
 * response is otherwise identical to a genuine success (same status, same shape) so a caller
 * cannot tell the two apart. The existing account gets a "you already have an account" email
 * directly; nothing here reveals that to whoever submitted the duplicate. */
export async function registerSupplier(payload: RegisterSupplierPayload): Promise<{ referenceCode: string | null }> {
  const res = await fetch(`${API_BASE_URL}/api/v1/auth/register`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })
  return parseJsonOrThrow(res)
}

/** fetch wrapper that attaches the in-memory access token and retries once via the
 * httpOnly refresh cookie on a 401 before giving up (docs/architecture ASVS L2 token handling). */
export async function apiFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const doFetch = () => {
    const token = useAuthStore.getState().accessToken
    return fetch(`${API_BASE_URL}${path}`, {
      ...init,
      credentials: 'include',
      headers: {
        ...(init.headers ?? {}),
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
    })
  }

  let res = await doFetch()
  if (res.status === 401) {
    const refreshed = await refresh()
    if (refreshed) {
      useAuthStore.getState().setSession(refreshed.accessToken)
      res = await doFetch()
    } else {
      useAuthStore.getState().clearSession()
    }
  }
  return res
}
