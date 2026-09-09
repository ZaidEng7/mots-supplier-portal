import { create } from 'zustand'

export interface AuthClaims {
  userId: string
  email: string
  supplierId?: string
  organizationId?: string
  permissions: string[]
}

interface AuthState {
  accessToken: string | null
  claims: AuthClaims | null
  status: 'idle' | 'authenticated' | 'unauthenticated'
  /**
   * SCR-040. True when a session that WAS working stopped working - the refresh cookie expired or was
   * revoked while the user was on a page. Distinct from `status: 'unauthenticated'`, which is also
   * true of someone who simply has not signed in yet: the first deserves an overlay over the work in
   * progress, the second deserves the login screen.
   */
  expired: boolean
  /** The address to re-authenticate as, kept so the overlay does not ask a user who they are. In
   *  memory only, and cleared with the session it belonged to. */
  lastEmail: string | null
  setSession: (accessToken: string) => void
  clearSession: () => void
  /** Called when a refresh fails under an established session. */
  expireSession: () => void
}

/** Decodes JWT claims client-side for display/routing only — never trust this for authorization,
 * the API re-validates and enforces every permission server-side. */
function decodeClaims(accessToken: string): AuthClaims | null {
  try {
    const payload = accessToken.split('.')[1]
    const json = JSON.parse(atob(payload.replace(/-/g, '+').replace(/_/g, '/')))
    // The claim arrives as an array when there are several and as a bare string when there is one,
    // which is how JWT claims are serialised. Neither shape is an error; both mean "these permissions".
    const permissions: string[] = toPermissionList(json.perms)
    return {
      userId: json.sub,
      email: json.email,
      supplierId: json.supplierId,
      organizationId: json.organizationId,
      permissions,
    }
  } catch {
    return null
  }
}

/** A JWT claim holding several values arrives as an array and one holding a single value as a string. */
function toPermissionList(claim: unknown): string[] {
  if (Array.isArray(claim)) return claim as string[]
  if (typeof claim === 'string' && claim !== '') return [claim]
  return []
}

export const useAuthStore = create<AuthState>((set, get) => ({
  accessToken: null,
  claims: null,
  status: 'idle',
  expired: false,
  lastEmail: null,
  setSession: (accessToken) => {
    const claims = decodeClaims(accessToken)
    set({ accessToken, claims, status: 'authenticated', expired: false, lastEmail: claims?.email ?? get().lastEmail })
  },
  // A deliberate sign-out. Clears the remembered address too: the next person at this browser is not
  // the last one, and offering their email back would be a small leak for no gain.
  clearSession: () => set({ accessToken: null, claims: null, status: 'unauthenticated', expired: false, lastEmail: null }),
  expireSession: () =>
    set({
      accessToken: null,
      claims: null,
      status: 'unauthenticated',
      // Only an established session can expire. Without this guard a 401 on a public page - a bad
      // password on the login form, say - would raise a "your session expired" overlay over the login
      // screen of someone who never had one.
      expired: get().status === 'authenticated',
      lastEmail: get().claims?.email ?? get().lastEmail,
    }),
}))
