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
  setSession: (accessToken: string) => void
  clearSession: () => void
}

/** Decodes JWT claims client-side for display/routing only — never trust this for authorization,
 * the API re-validates and enforces every permission server-side. */
function decodeClaims(accessToken: string): AuthClaims | null {
  try {
    const payload = accessToken.split('.')[1]
    const json = JSON.parse(atob(payload.replace(/-/g, '+').replace(/_/g, '/')))
    const permissions: string[] = Array.isArray(json.permission) ? json.permission : json.permission ? [json.permission] : []
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

export const useAuthStore = create<AuthState>((set) => ({
  accessToken: null,
  claims: null,
  status: 'idle',
  setSession: (accessToken) =>
    set({ accessToken, claims: decodeClaims(accessToken), status: 'authenticated' }),
  clearSession: () => set({ accessToken: null, claims: null, status: 'unauthenticated' }),
}))
