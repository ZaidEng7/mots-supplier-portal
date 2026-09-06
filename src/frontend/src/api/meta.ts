import { API_BASE_URL } from './auth'

/** SCR-908. API-ARCHITECTURE.md's ops row: `GET /api/v1/meta` (build/version/commit). */
export interface Meta {
  version: string | null
  /** Null when the build carries no source revision - a local `dotnet run`, for instance. */
  commit: string | null
  /** SCR-044. Null unless an operator has configured a notice. */
  maintenance: MaintenanceNotice | null
}

export interface MaintenanceNotice {
  messageAr: string | null
  messageEn: string | null
  /** Both optional, both free text as configured. Displayed, never compared against a clock here - the
   *  operator who wrote the notice is the one who knows when it applies. */
  from: string | null
  to: string | null
}

/**
 * Plain `fetch`, not `apiFetch`. The endpoint is anonymous and the about screen has to render for
 * someone who cannot sign in; going through apiFetch would attach a token it does not need and, worse,
 * could raise SCR-040's expiry overlay over a public page.
 */
export async function getMeta(): Promise<Meta> {
  const res = await fetch(`${API_BASE_URL}/api/v1/meta`)
  if (!res.ok) throw new Error(`meta ${res.status}`)
  return (await res.json()) as Meta
}
