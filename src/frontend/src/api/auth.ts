import { forgetETags, lookupETag, ownerPrefixOf, rememberETag } from './etags'
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

/** MSP-73: supplierCode is null when the email/registration number was already taken - the
 * response is otherwise identical to a genuine success (same status, same shape) so a caller
 * cannot tell the two apart. The existing account gets a "you already have an account" email
 * directly; nothing here reveals that to whoever submitted the duplicate. */
export async function registerSupplier(payload: RegisterSupplierPayload): Promise<{ supplierCode: string | null }> {
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
  const method = (init.method ?? 'GET').toUpperCase()
  const isMutation = method !== 'GET' && method !== 'HEAD'

  /*
   * T-053/§8.2.5: "The SPA generates one key per user submission intent (e.g. per 'Submit' click) via
   * crypto.randomUUID()".
   *
   * Generated ONCE here, outside doFetch, and that placement is the whole point: doFetch is called
   * again after a 401 refresh, and a key regenerated on the retry would be a second intent - the
   * server would process the submission twice, which is the exact failure this header exists to
   * prevent.
   *
   * Sent on every mutation rather than only on the three §8.2 requires. Harmless where the server does
   * not read it, and it means a route that starts requiring one does not silently 428 the SPA.
   */
  const idempotencyKey = isMutation ? crypto.randomUUID() : undefined

  // T-030 split (2): where this write's precondition came from, captured BEFORE the write clears it.
  // The response's fresh version goes back to the same place, so a sibling child collection can still
  // find it - see ownerPrefixOf for the defect this closes.
  const preconditionPrefix = isMutation ? ownerPrefixOf(path) : undefined

  const doFetch = () => {
    const token = useAuthStore.getState().accessToken

    // §8.1: the version this caller last read travels back as If-Match on a mutation. Attached here
    // rather than at each call site - see api/etags.ts. An explicit If-Match always wins, so a
    // caller that has just reconciled a 412 can send the version it chose.
    const ifMatch = isMutation ? lookupETag(path) : undefined
    const headers: Record<string, string> = {
      ...(init.headers as Record<string, string> | undefined ?? {}),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    }
    if (ifMatch && !('If-Match' in headers)) headers['If-Match'] = ifMatch
    if (idempotencyKey && !('Idempotency-Key' in headers)) headers['Idempotency-Key'] = idempotencyKey

    return fetch(`${API_BASE_URL}${path}`, { ...init, credentials: 'include', headers })
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
  // T-030 splits (3) and (2): FORGET first, then put the response's version back in BOTH places.
  //
  // A mutation moves the resource on, so the version cached before it is stale - keeping it would turn
  // the caller's next save into a 412 nobody can explain. But the child-write routes now return the
  // version the write produced (see WithFreshETag), and dropping THAT would make a supplier editing two
  // contacts in a row hit 428 on the second until a re-read landed. So the order matters: clear the old,
  // then keep the new when there is one.
  //
  // Split (2) added the second `rememberETag`. Filing the fresh version only under the WRITE path left
  // it invisible to a sibling child collection: after adding an RFQ item the version sat at
  // `/rfqs/RFQ-1/items`, and adding a requirement walked up to `/rfqs/RFQ-1`, found the entry deleted,
  // and sent no If-Match at all - a 428 on the officer's second edit. Writing it back to the prefix the
  // precondition came from keeps the aggregate's version reachable from every child of it.
  const freshETag = res.headers.get('ETag')
  if (isMutation && res.ok) {
    forgetETags(path)
    if (preconditionPrefix) rememberETag(preconditionPrefix, freshETag)
  }
  rememberETag(path, freshETag)

  // A 428 means this client failed to send a header it should always send: a bug in the transport
  // above, not a state the user can do anything about. Surfaced loudly rather than folded into the
  // generic error path, where it would reach a supplier as an unexplained failure to save.
  if (res.status === 428) {
    console.error(`[concurrency] ${method} ${path} was refused for a missing If-Match. ` +
      'The resource was mutated without a prior read, or its ETag was never stored.')
  }

  return res
}
